using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IberiaSmartDisc.Core.Json;
using IberiaSmartDisc.Core.Manifest;
using IberiaSmartDisc.Core.Platforms;

namespace IberiaSmartDisc.Core.Configuration
{
    /// <summary>Lo que el programa recuerda de un juego en este PC (nunca en el disco).</summary>
    public sealed class GameRecord
    {
        public GameRecord(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }

        public string Name { get; set; }

        public string? Edition { get; set; }

        /// <summary>Clave del candidato elegido ("steam:620", "local:D:\…").</summary>
        public string? PreferredKey { get; set; }

        /// <summary>«Usar siempre esta opción»: abrir sin volver a preguntar.</summary>
        public bool AlwaysUse { get; set; }

        public string? LocalExecutable { get; set; }

        public DateTime? FirstSeenUtc { get; set; }

        public DateTime? LastSeenUtc { get; set; }

        public DateTime? LastPlayedUtc { get; set; }

        public LaunchKind? LastPlatform { get; set; }

        public int TimesInserted { get; set; }

        public List<string> DiscIds { get; } = new List<string>();

        public string DisplayName => Edition == null ? Name : Name + " · " + Edition;

        public LaunchKind? PreferredKind => LaunchKinds.KindOfKey(PreferredKey);
    }

    /// <summary>
    /// games.json: juegos recordados, preferencias e historial. No guarda
    /// «confianza» en ejecutables de discos: el manifiesto se puede copiar a otro
    /// disco con otro programa, así que se pregunta cada vez.
    /// </summary>
    public sealed class GameLibrary
    {
        public const int CurrentVersion = 1;
        private const int MaxDiscIdsPerGame = 20;

        private readonly Dictionary<string, GameRecord> _games = new Dictionary<string, GameRecord>(StringComparer.Ordinal);

        public string? LastGameId { get; set; }

        public IEnumerable<GameRecord> Games => _games.Values.OrderByDescending(g => g.LastSeenUtc ?? DateTime.MinValue);

        public int Count => _games.Count;

        public GameRecord? Find(string gameId) => _games.TryGetValue(gameId, out var record) ? record : null;

        public GameRecord RecordInsertion(DiscManifest manifest, DateTime nowUtc)
        {
            if (!_games.TryGetValue(manifest.GameId, out var record))
            {
                record = new GameRecord(manifest.GameId, manifest.GameName) { FirstSeenUtc = nowUtc };
                _games[manifest.GameId] = record;
            }
            record.Name = manifest.GameName;
            record.Edition = manifest.Edition;
            record.LastSeenUtc = nowUtc;
            record.TimesInserted++;
            if (manifest.DiscId != null && !record.DiscIds.Contains(manifest.DiscId))
            {
                record.DiscIds.Add(manifest.DiscId);
                while (record.DiscIds.Count > MaxDiscIdsPerGame) record.DiscIds.RemoveAt(0);
            }
            LastGameId = manifest.GameId;
            return record;
        }

        public void RecordLaunch(string gameId, LaunchKind kind, DateTime nowUtc)
        {
            var record = Find(gameId);
            if (record == null) return;
            record.LastPlayedUtc = nowUtc;
            record.LastPlatform = kind;
        }

        public void SetPreference(string gameId, LaunchCandidate candidate, bool alwaysUse)
        {
            var record = Find(gameId);
            if (record == null) return;
            record.PreferredKey = candidate.Key;
            record.AlwaysUse = alwaysUse;
            if (candidate.Kind == LaunchKind.Local) record.LocalExecutable = candidate.Target.ExecutablePath;
        }

        public void ClearPreference(string gameId)
        {
            var record = Find(gameId);
            if (record == null) return;
            record.PreferredKey = null;
            record.AlwaysUse = false;
        }

        public bool Forget(string gameId)
        {
            if (LastGameId == gameId) LastGameId = null;
            return _games.Remove(gameId);
        }

        public JsonNode ToJson()
        {
            var games = JsonNode.NewObject();
            foreach (var record in _games.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                games.Set(record.Id, JsonNode.NewObject()
                    .Set("name", record.Name)
                    .Set("edition", record.Edition)
                    .Set("preferredMethod", record.PreferredKind is LaunchKind kind ? LaunchKinds.ToId(kind) : null)
                    .Set("preferredKey", record.PreferredKey)
                    .Set("alwaysUse", record.AlwaysUse)
                    .Set("executable", record.LocalExecutable)
                    .Set("firstSeen", FormatDate(record.FirstSeenUtc))
                    .Set("lastSeen", FormatDate(record.LastSeenUtc))
                    .Set("lastPlayed", FormatDate(record.LastPlayedUtc))
                    .Set("lastPlatform", record.LastPlatform is LaunchKind last ? LaunchKinds.ToId(last) : null)
                    .Set("timesInserted", record.TimesInserted)
                    .Set("discIds", JsonNode.FromStrings(record.DiscIds)));
            }

            return JsonNode.NewObject()
                .Set("version", CurrentVersion)
                .Set("lastGameId", LastGameId)
                .Set("games", games);
        }

        public static GameLibrary FromJson(JsonNode? json)
        {
            var library = new GameLibrary();
            if (json == null || json.Kind != JsonKind.Object) return library;

            var games = json.GetObject("games");
            if (games != null)
            {
                foreach (var property in games.Properties)
                {
                    var node = property.Value;
                    if (!ManifestRules.IsGameId(property.Key) || node.Kind != JsonKind.Object) continue;
                    var record = new GameRecord(property.Key, node.GetString("name") ?? property.Key)
                    {
                        Edition = node.GetString("edition"),
                        PreferredKey = node.GetString("preferredKey"),
                        AlwaysUse = node.GetBoolean("alwaysUse") ?? false,
                        LocalExecutable = node.GetString("executable"),
                        FirstSeenUtc = ParseDate(node.GetString("firstSeen")),
                        LastSeenUtc = ParseDate(node.GetString("lastSeen")),
                        LastPlayedUtc = ParseDate(node.GetString("lastPlayed")),
                        TimesInserted = (int)Math.Max(0, Math.Min(int.MaxValue, node.GetInt64("timesInserted") ?? 0)),
                    };
                    if (LaunchKinds.TryParse(node.GetString("lastPlatform"), out var lastPlatform)) record.LastPlatform = lastPlatform;
                    if (record.PreferredKey != null && LaunchKinds.KindOfKey(record.PreferredKey) == null) record.PreferredKey = null;
                    var discIds = node.GetArray("discIds");
                    if (discIds != null)
                    {
                        foreach (var item in discIds.Items.Take(MaxDiscIdsPerGame))
                        {
                            var discId = item.AsString();
                            if (ManifestRules.IsDiscId(discId) && !record.DiscIds.Contains(discId!)) record.DiscIds.Add(discId!);
                        }
                    }
                    library._games[record.Id] = record;
                }
            }

            var lastGameId = json.GetString("lastGameId");
            library.LastGameId = lastGameId != null && library._games.ContainsKey(lastGameId) ? lastGameId : null;

            return library;
        }

        private static string? FormatDate(DateTime? value) =>
            value?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        private static DateTime? ParseDate(string? value)
        {
            if (value == null) return null;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
                ? date
                : (DateTime?)null;
        }
    }
}
