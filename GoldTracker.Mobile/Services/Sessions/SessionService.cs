using System.Text.Json;
using Archery.Shared.Models;

namespace GoldTracker.Mobile.Services.Sessions
{
    public class SessionService : ISessionService
    {
        private readonly string _sessionsDir;

        public SessionService()
        {
            _sessionsDir = Path.Combine(FileSystem.AppDataDirectory, "sessions");
            if (!Directory.Exists(_sessionsDir))
            {
                Directory.CreateDirectory(_sessionsDir);
            }
        }

        public async Task<List<Session>> GetSessionsAsync()
        {
            var sessions = new List<Session>();
            var files = Directory.GetFiles(_sessionsDir, "*.json");

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var session = JsonSerializer.Deserialize<Session>(json);
                    if (session != null)
                    {
                        sessions.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading session {file}: {ex.Message}");
                }
            }

            return sessions.OrderByDescending(s => s.StartTime).ToList();
        }

        public async Task<Session?> GetSessionAsync(Guid id)
        {
            var filePath = Path.Combine(_sessionsDir, $"{id}.json");
            if (!File.Exists(filePath)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<Session>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading session {id}: {ex.Message}");
                return null;
            }
        }

        public async Task SaveSessionAsync(Session session)
        {
            try
            {
                var filePath = Path.Combine(_sessionsDir, $"{session.Id}.json");
                var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving session {session.Id}: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteSessionAsync(Guid id)
        {
            var filePath = Path.Combine(_sessionsDir, $"{id}.json");
            if (File.Exists(filePath))
            {
                await Task.Run(() => File.Delete(filePath));
            }
        }
    }
}
