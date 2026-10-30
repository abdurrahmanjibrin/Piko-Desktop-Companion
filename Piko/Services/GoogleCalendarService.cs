using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Piko.Models;

namespace Piko.Services;

public sealed class GoogleCalendarService
{
    private readonly HttpClient _httpClient = new();
    private static readonly string[] AccountColors = { "#3B82F6", "#8B5CF6", "#10B981", "#F59E0B", "#EC4899", "#06B6D4" };

    public event Action<string>? DiagnosticLogged;

    // Default public client ID for desktop OAuth flow if user hasn't set custom credentials
    public const string DefaultClientId = "874744772093-g1gqu6bov5ekeb0k571n4r3f9d5v76u0.apps.googleusercontent.com";

    private void Log(string message) => DiagnosticLogged?.Invoke($"[GoogleCalendarService] {message}");

    public async Task<GoogleAccountEntry?> AuthenticateWithLoopbackAsync(string? customClientId = null, string? customClientSecret = null)
    {
        if (string.IsNullOrWhiteSpace(customClientId))
        {
            Log("Google OAuth requires a custom Client ID from Google Cloud Console.");
            return null;
        }

        string clientId = customClientId.Trim();
        string clientSecret = customClientSecret?.Trim() ?? string.Empty;

        int port = 58432;
        string redirectUri = $"http://127.0.0.1:{port}/oauth2callback/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);

        try
        {
            listener.Start();
            Log($"HttpListener started on {redirectUri}");
        }
        catch (Exception ex)
        {
            Log($"Failed to start HttpListener: {ex.Message}");
            return null;
        }

        string state = Guid.NewGuid().ToString("N");
        string scope = Uri.EscapeDataString("https://www.googleapis.com/auth/calendar.events.readonly https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/userinfo.profile");

        string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                         $"client_id={Uri.EscapeDataString(clientId)}&" +
                         $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                         $"response_type=code&" +
                         $"scope={scope}&" +
                         $"access_type=offline&" +
                         $"prompt=select_account%20consent&" +
                         $"state={state}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });
            Log("Launched browser for Google OAuth sign-in");
        }
        catch (Exception ex)
        {
            Log($"Failed to launch browser: {ex.Message}");
            listener.Stop();
            return null;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2.5));

        try
        {
            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token));

            if (completedTask != contextTask)
            {
                Log("Google OAuth timed out waiting for user confirmation.");
                listener.Stop();
                return null;
            }

            var context = await contextTask;
            var request = context.Request;
            var response = context.Response;

            string? code = request.QueryString["code"];
            string? returnedState = request.QueryString["state"];
            string? error = request.QueryString["error"];

            var responseHtml = @"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><title>Piko — Google Calendar Linked</title></head>
<body style='background:#0f0d18;color:#f1edff;font-family:system-ui,-apple-system,sans-serif;text-align:center;padding:60px 20px;'>
  <div style='max-width:440px;margin:0 auto;background:#1a1628;padding:32px;border-radius:20px;border:1px solid #3d2f68;box-shadow:0 10px 30px rgba(0,0,0,0.5);'>
    <div style='font-size:42px;margin-bottom:12px;'>✨</div>
    <h2 style='margin:0 0 8px 0;font-size:20px;color:#fff;'>Akun Google Berhasil Ditautkan!</h2>
    <p style='color:#a5b4fc;font-size:13px;line-height:1.5;margin:0 0 20px 0;'>Kalender Piko Anda kini tersinkronisasi. Anda dapat menutup tab browser ini sekarang.</p>
    <div style='display:inline-block;padding:8px 16px;background:#251e38;border-radius:10px;font-size:11px;color:#cbd5e1;'>Kembali ke Piko Desktop Companion</div>
  </div>
</body>
</html>";

            byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
            listener.Stop();

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            {
                Log($"Google OAuth failed with error: {error ?? "no code returned"}");
                return null;
            }

            return await ExchangeCodeForTokenAsync(code, redirectUri, clientId, clientSecret);
        }
        catch (Exception ex)
        {
            Log($"Error during OAuth loopback handling: {ex.Message}");
            try { listener.Stop(); } catch { }
            return null;
        }
    }

    private async Task<GoogleAccountEntry?> ExchangeCodeForTokenAsync(string code, string redirectUri, string clientId, string clientSecret)
    {
        try
        {
            var tokenRequestParams = new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            };

            using var content = new FormUrlEncodedContent(tokenRequestParams);
            var tokenResponse = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content);
            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();

            if (!tokenResponse.IsSuccessStatusCode)
            {
                Log($"Token exchange failed: {tokenResponse.StatusCode} -> {tokenJson}");
                return null;
            }

            using var doc = JsonDocument.Parse(tokenJson);
            var root = doc.RootElement;

            string accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() ?? "" : "";
            string refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
            int expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

            if (string.IsNullOrEmpty(accessToken)) return null;

            // Fetch user profile info
            using var userInfoReq = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            userInfoReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var userResponse = await _httpClient.SendAsync(userInfoReq);

            string email = "user@gmail.com";
            string displayName = "Google User";

            if (userResponse.IsSuccessStatusCode)
            {
                var userJson = await userResponse.Content.ReadAsStringAsync();
                using var userDoc = JsonDocument.Parse(userJson);
                var uRoot = userDoc.RootElement;
                if (uRoot.TryGetProperty("email", out var em)) email = em.GetString() ?? email;
                if (uRoot.TryGetProperty("name", out var nm)) displayName = nm.GetString() ?? displayName;
            }

            return new GoogleAccountEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Email = email,
                DisplayName = displayName,
                AccountColor = AccountColors[Random.Shared.Next(AccountColors.Length)],
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                TokenExpiry = DateTime.Now.AddSeconds(expiresIn - 60),
                IsActive = true,
                LastSyncTime = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Log($"ExchangeCodeForTokenAsync error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<CalendarEventItem>> FetchEventsForAccountAsync(GoogleAccountEntry account, DateTime startRange, DateTime endRange, string? customClientId = null, string? customClientSecret = null)
    {
        var result = new List<CalendarEventItem>();

        // If no access token (e.g. sample mock account), return sample events
        if (string.IsNullOrWhiteSpace(account.AccessToken))
        {
            return GetSampleEventsForAccount(account);
        }

        try
        {
            await RefreshTokenIfNeededAsync(account, customClientId, customClientSecret);

            string timeMin = Uri.EscapeDataString(startRange.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
            string timeMax = Uri.EscapeDataString(endRange.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));

            string url = $"https://www.googleapis.com/calendar/v3/calendars/primary/events?timeMin={timeMin}&timeMax={timeMax}&singleEvents=true&orderBy=startTime&maxResults=250";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);

            var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                Log($"FetchEvents failed for {account.Email}: {resp.StatusCode}");
                return GetSampleEventsForAccount(account);
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    string id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                    string summary = item.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() ?? "(No Title)" : "(No Title)";
                    string description = item.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "";
                    string location = item.TryGetProperty("location", out var locProp) ? locProp.GetString() ?? "" : "";
                    string htmlLink = item.TryGetProperty("htmlLink", out var linkProp) ? linkProp.GetString() ?? "" : "";
                    string meetUrl = "";

                    if (item.TryGetProperty("hangoutLink", out var hLink))
                    {
                        meetUrl = hLink.GetString() ?? "";
                    }

                    DateTime sTime = DateTime.Today;
                    DateTime eTime = DateTime.Today.AddHours(1);
                    bool isAllDay = false;

                    if (item.TryGetProperty("start", out var startProp))
                    {
                        if (startProp.TryGetProperty("dateTime", out var dt) && DateTime.TryParse(dt.GetString(), out var parsedS))
                        {
                            sTime = parsedS.ToLocalTime();
                        }
                        else if (startProp.TryGetProperty("date", out var d) && DateTime.TryParse(d.GetString(), out var parsedDate))
                        {
                            sTime = parsedDate;
                            isAllDay = true;
                        }
                    }

                    if (item.TryGetProperty("end", out var endProp))
                    {
                        if (endProp.TryGetProperty("dateTime", out var dt) && DateTime.TryParse(dt.GetString(), out var parsedE))
                        {
                            eTime = parsedE.ToLocalTime();
                        }
                        else if (endProp.TryGetProperty("date", out var d) && DateTime.TryParse(d.GetString(), out var parsedDate))
                        {
                            eTime = parsedDate;
                        }
                    }

                    result.Add(new CalendarEventItem
                    {
                        Id = id,
                        AccountEmail = account.Email,
                        AccountColor = account.AccountColor,
                        Title = summary,
                        Description = description,
                        StartTime = sTime,
                        EndTime = eTime,
                        IsAllDay = isAllDay,
                        Location = location,
                        MeetUrl = meetUrl,
                        HtmlLink = htmlLink
                    });
                }
            }

            account.LastSyncTime = DateTime.Now;
        }
        catch (Exception ex)
        {
            Log($"Error fetching events for {account.Email}: {ex.Message}");
            return GetSampleEventsForAccount(account);
        }

        return result;
    }

    private async Task RefreshTokenIfNeededAsync(GoogleAccountEntry account, string? customClientId, string? customClientSecret)
    {
        if (string.IsNullOrEmpty(account.RefreshToken)) return;
        if (DateTime.Now < account.TokenExpiry) return;

        string clientId = !string.IsNullOrWhiteSpace(customClientId) ? customClientId : DefaultClientId;
        string clientSecret = customClientSecret ?? string.Empty;

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = account.RefreshToken,
                ["grant_type"] = "refresh_token"
            });

            var resp = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("access_token", out var at))
                {
                    account.AccessToken = at.GetString() ?? account.AccessToken;
                    int exp = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
                    account.TokenExpiry = DateTime.Now.AddSeconds(exp - 60);
                    Log($"Refreshed access token for {account.Email}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to refresh token: {ex.Message}");
        }
    }

    public async Task<List<CalendarEventItem>> FetchEventsAsync(GoogleAccountEntry account)
    {
        if (account.SyncMethod == "iCal" || !string.IsNullOrWhiteSpace(account.IcalUrl))
        {
            return await FetchEventsFromIcalUrlAsync(account);
        }

        var start = DateTime.Today.AddDays(-14);
        var end = DateTime.Today.AddDays(45);
        return await FetchEventsForAccountAsync(account, start, end);
    }

    public async Task<List<CalendarEventItem>> FetchEventsFromIcalUrlAsync(GoogleAccountEntry account)
    {
        var result = new List<CalendarEventItem>();
        if (string.IsNullOrWhiteSpace(account.IcalUrl)) return result;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, account.IcalUrl.Trim());
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PikoCompanion/0.2");
            var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                Log($"iCal fetch failed for {account.Email}: {resp.StatusCode}");
                return result;
            }

            var icsContent = await resp.Content.ReadAsStringAsync();
            return ParseIcsCalendar(icsContent, account);
        }
        catch (Exception ex)
        {
            Log($"FetchEventsFromIcalUrlAsync error: {ex.Message}");
            return result;
        }
    }

    public List<CalendarEventItem> ParseIcsCalendar(string icsText, GoogleAccountEntry account)
    {
        var events = new List<CalendarEventItem>();
        if (string.IsNullOrWhiteSpace(icsText)) return events;

        // RFC 5545 Unfolding: Lines starting with whitespace are part of the previous line
        var rawLines = icsText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var unfoldedLines = new List<string>();
        foreach (var raw in rawLines)
        {
            if (raw.StartsWith(' ') || raw.StartsWith('\t'))
            {
                if (unfoldedLines.Count > 0)
                {
                    unfoldedLines[^1] += raw[1..];
                }
            }
            else
            {
                unfoldedLines.Add(raw);
            }
        }

        CalendarEventItem? current = null;

        foreach (var line in unfoldedLines)
        {
            var trimmed = line.Trim();
            if (trimmed == "BEGIN:VEVENT")
            {
                current = new CalendarEventItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    AccountEmail = account.Email,
                    AccountColor = account.AccountColor
                };
            }
            else if (trimmed == "END:VEVENT")
            {
                if (current != null)
                {
                    if (string.IsNullOrWhiteSpace(current.Title)) current.Title = "(No Title)";
                    if (current.EndTime <= current.StartTime && !current.IsAllDay)
                    {
                        current.EndTime = current.StartTime.AddHours(1);
                    }
                    events.Add(current);
                }
                current = null;
            }
            else if (current != null)
            {
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    string prop = trimmed[..colonIdx];
                    string val = trimmed[(colonIdx + 1)..];

                    // Decode standard iCal escaping
                    val = val.Replace(@"\,", ",").Replace(@"\;", ";").Replace(@"\n", "\n").Replace(@"\N", "\n").Replace(@"\\", @"\");

                    if (prop.StartsWith("SUMMARY"))
                    {
                        current.Title = val;
                    }
                    else if (prop.StartsWith("DESCRIPTION"))
                    {
                        current.Description = val;
                        // Search for Google Meet link inside description if not found yet
                        if (string.IsNullOrEmpty(current.MeetUrl))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(val, @"https://meet\.google\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}");
                            if (match.Success)
                            {
                                current.MeetUrl = match.Value;
                            }
                        }
                    }
                    else if (prop.StartsWith("LOCATION"))
                    {
                        current.Location = val;
                        if (string.IsNullOrEmpty(current.MeetUrl) && val.Contains("meet.google.com"))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(val, @"https://meet\.google\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}");
                            if (match.Success) current.MeetUrl = match.Value;
                        }
                    }
                    else if (prop.StartsWith("URL"))
                    {
                        if (val.Contains("meet.google.com")) current.MeetUrl = val;
                        else current.HtmlLink = val;
                    }
                    else if (prop.StartsWith("UID"))
                    {
                        current.Id = val;
                    }
                    else if (prop.StartsWith("DTSTART"))
                    {
                        var (dt, isAllDay) = ParseIcsDateTime(val, prop);
                        current.StartTime = dt;
                        current.IsAllDay = isAllDay;
                    }
                    else if (prop.StartsWith("DTEND"))
                    {
                        var (dt, _) = ParseIcsDateTime(val, prop);
                        current.EndTime = dt;
                    }
                    else if (prop.StartsWith("X-GOOGLE-CONFERENCE") || prop.StartsWith("CONFERENCE"))
                    {
                        if (val.Contains("meet.google.com"))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(val, @"https://meet\.google\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}");
                            if (match.Success) current.MeetUrl = match.Value;
                        }
                    }
                }
            }
        }

        return events;
    }

    private static (DateTime DateTime, bool IsAllDay) ParseIcsDateTime(string val, string prop)
    {
        val = val.Trim();
        bool isAllDay = prop.Contains("VALUE=DATE") || val.Length == 8;

        if (isAllDay && val.Length >= 8)
        {
            if (int.TryParse(val[..4], out int y) &&
                int.TryParse(val.Substring(4, 2), out int m) &&
                int.TryParse(val.Substring(6, 2), out int d))
            {
                return (new DateTime(y, m, d), true);
            }
        }

        if (val.Contains('T') && val.Length >= 15)
        {
            try
            {
                int y = int.Parse(val[..4]);
                int m = int.Parse(val.Substring(4, 2));
                int d = int.Parse(val.Substring(6, 2));
                int hh = int.Parse(val.Substring(9, 2));
                int mm = int.Parse(val.Substring(11, 2));
                int ss = int.Parse(val.Substring(13, 2));

                if (val.EndsWith('Z'))
                {
                    var utc = new DateTime(y, m, d, hh, mm, ss, DateTimeKind.Utc);
                    return (utc.ToLocalTime(), false);
                }
                return (new DateTime(y, m, d, hh, mm, ss), false);
            }
            catch
            {
                // Fall through
            }
        }

        if (DateTime.TryParse(val, out var parsed))
        {
            return (parsed, false);
        }

        return (DateTime.Today, false);
    }

    public List<CalendarEventItem> GetSampleEvents()
    {
        var mockAccount = new GoogleAccountEntry
        {
            Email = "abd.ahmad@gmail.com",
            DisplayName = "Abdur Rahman",
            AccountColor = "#3B82F6"
        };
        return GetSampleEventsForAccount(mockAccount);
    }

    /// <summary>
    /// Generates accurate initial sample events derived directly from the user's provided Google Calendar screenshot
    /// </summary>
    public List<CalendarEventItem> GetSampleEventsForAccount(GoogleAccountEntry account)
    {
        var today = DateTime.Today;
        var color = account.AccountColor;

        return new List<CalendarEventItem>
        {
            new()
            {
                Id = "sample-1",
                AccountEmail = account.Email,
                AccountColor = color,
                Title = "[Asah 2026] Pengisian Biweekly Feedback #1",
                Description = "Pengisian form evaluasi progres dwi-mingguan program Asah 2026",
                StartTime = today.AddHours(10),
                EndTime = today.AddHours(11.5),
                IsAllDay = false,
                Location = "Google Meet",
                MeetUrl = "https://meet.google.com/xyz-asah-feed",
                HtmlLink = "https://calendar.google.com"
            },
            new()
            {
                Id = "sample-2",
                AccountEmail = account.Email,
                AccountColor = color,
                Title = "1pm [Asah 2026] Kupas Tuntas Portofolio",
                Description = "Sesi bedah portofolio dan review milestone bersama mentor",
                StartTime = today.AddHours(13),
                EndTime = today.AddHours(14.5),
                IsAllDay = false,
                Location = "Google Meet",
                MeetUrl = "https://meet.google.com/abc-port-rev",
                HtmlLink = "https://calendar.google.com"
            },
            new()
            {
                Id = "sample-3",
                AccountEmail = account.Email,
                AccountColor = color,
                Title = "3:30pm ILT-AI1-A26-AD: Fundamental AI & Agentic",
                Description = "Instructor-Led Training sesi pengenalan arsitektur AI dan agentic workflows",
                StartTime = today.AddHours(15.5),
                EndTime = today.AddHours(17),
                IsAllDay = false,
                Location = "Zoom Meeting",
                MeetUrl = "https://meet.google.com/ilt-ai-2026",
                HtmlLink = "https://calendar.google.com"
            },
            new()
            {
                Id = "sample-4",
                AccountEmail = account.Email,
                AccountColor = color,
                Title = "5pm [Asah 2026 - Reminder] Daily Logbook Submission",
                Description = "Pengingat rutin untuk submit logbook harian sebelum jam 6 sore",
                StartTime = today.AddHours(17),
                EndTime = today.AddHours(17.5),
                IsAllDay = false,
                HtmlLink = "https://calendar.google.com"
            },
            new()
            {
                Id = "sample-5",
                AccountEmail = account.Email,
                AccountColor = color,
                Title = "8pm [Asah 2026 - Reminder] Peer Code Review",
                Description = "Sesi tukar review kode tugas companion desktop",
                StartTime = today.AddHours(20),
                EndTime = today.AddHours(21),
                IsAllDay = false,
                HtmlLink = "https://calendar.google.com"
            },
            new()
            {
                Id = "sample-6",
                AccountEmail = account.Email,
                AccountColor = color,
                Title = "10am [Asah 2026] Pengisian Biweekly Feedback #2",
                Description = "Evaluasi progres tahap kedua program Asah 2026",
                StartTime = today.AddDays(7).AddHours(10),
                EndTime = today.AddDays(7).AddHours(11.5),
                IsAllDay = false,
                MeetUrl = "https://meet.google.com/feedback-two-2026",
                HtmlLink = "https://calendar.google.com"
            },
            new()
            {
                Id = "sample-7",
                AccountEmail = account.Email,
                AccountColor = color,
                Title = "3pm [Mandatory] Asah 2026 Monthly Sync",
                Description = "Pertemuan bulanan seluruh peserta dan fasilitator Asah 2026",
                StartTime = today.AddDays(3).AddHours(15),
                EndTime = today.AddDays(3).AddHours(16.5),
                IsAllDay = false,
                MeetUrl = "https://meet.google.com/sync-monthly-asah",
                HtmlLink = "https://calendar.google.com"
            }
        };
    }
}
