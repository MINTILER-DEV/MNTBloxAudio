using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MNTBloxAudio.Core.Models;

public sealed class ReplacementRule : INotifyPropertyChanged
{
    private const int CurrentPreparationVersion = 2;
    private string name = "New Rule";
    private string assetIdPattern = string.Empty;
    private string filePath = string.Empty;
    private bool isEnabled = true;
    private int gainPercent = 100;
    private string sourceAssetHash = string.Empty;
    private long sourceAssetLength;
    private string replacementFileHash = string.Empty;
    private long replacementFileLength;
    private DateTimeOffset? preparedAt;
    private int preparationVersion;
    private bool replacementSourceWasConverted;
    private bool isOriginalPresentInCache;
    private bool isReplacementPresentInCache;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public string AssetIdPattern
    {
        get => assetIdPattern;
        set => SetField(ref assetIdPattern, value);
    }

    public string FilePath
    {
        get => filePath;
        set => SetField(ref filePath, value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetField(ref isEnabled, value);
    }

    public int GainPercent
    {
        get => gainPercent;
        set => SetField(ref gainPercent, value);
    }

    public string SourceAssetHash
    {
        get => sourceAssetHash;
        set => SetField(ref sourceAssetHash, value);
    }

    public long SourceAssetLength
    {
        get => sourceAssetLength;
        set
        {
            if (sourceAssetLength == value)
            {
                return;
            }

            sourceAssetLength = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourceAssetSizeDisplay));
        }
    }

    public string ReplacementFileHash
    {
        get => replacementFileHash;
        set => SetField(ref replacementFileHash, value);
    }

    public long ReplacementFileLength
    {
        get => replacementFileLength;
        set
        {
            if (replacementFileLength == value)
            {
                return;
            }

            replacementFileLength = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReplacementFileSizeDisplay));
        }
    }

    public DateTimeOffset? PreparedAt
    {
        get => preparedAt;
        set => SetField(ref preparedAt, value);
    }

    public int PreparationVersion
    {
        get => preparationVersion;
        set => SetField(ref preparationVersion, value);
    }

    public bool ReplacementSourceWasConverted
    {
        get => replacementSourceWasConverted;
        set => SetField(ref replacementSourceWasConverted, value);
    }

    [JsonIgnore]
    public bool IsOriginalPresentInCache
    {
        get => isOriginalPresentInCache;
        set => SetField(ref isOriginalPresentInCache, value);
    }

    [JsonIgnore]
    public bool IsReplacementPresentInCache
    {
        get => isReplacementPresentInCache;
        set => SetField(ref isReplacementPresentInCache, value);
    }

    [JsonIgnore]
    public string SourceAssetSizeDisplay => FormatKilobytes(SourceAssetLength);

    [JsonIgnore]
    public string ReplacementFileSizeDisplay => FormatKilobytes(ReplacementFileLength);

    [JsonIgnore]
    public bool IsPrepared => PreparationVersion >= CurrentPreparationVersion
        && !string.IsNullOrWhiteSpace(SourceAssetHash)
        && !string.IsNullOrWhiteSpace(ReplacementFileHash)
        && SourceAssetLength > 0
        && ReplacementFileLength > 0
        && PreparedAt is not null;

    [JsonIgnore]
    public string FileNameDisplay => string.IsNullOrWhiteSpace(FilePath)
        ? "No source"
        : LooksLikeSongCode(FilePath)
            ? $"Code {FilePath.Trim().ToUpperInvariant()}"
        : TryGetRemoteDisplayName(FilePath);

    [JsonIgnore]
    public string ReplacementSourceNoteDisplay
    {
        get
        {
            if (LooksLikeSongCode(FilePath))
            {
                return "Resolved from song code";
            }

            return ReplacementSourceWasConverted
                ? "Auto-converted to MP3"
                : string.Empty;
        }
    }

    [JsonIgnore]
    public string StatusDisplay => !IsEnabled
        ? "Disabled"
        : !HasSourceReference(FilePath)
            ? "Missing Source"
            : IsPrepared
                ? "Ready"
                : "Needs Prep";

    [JsonIgnore]
    public string StatusTone => !IsEnabled
        ? "Muted"
        : !HasSourceReference(FilePath)
            ? "Warn"
            : IsPrepared
                ? "Good"
                : "Info";

    [JsonIgnore]
    public string SourceTypeDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                return "No source";
            }

            if (LooksLikeSongCode(FilePath))
            {
                return "Song code source";
            }

            return IsRemoteSource(FilePath) ? "Remote source" : "Local file source";
        }
    }

    [JsonIgnore]
    public string StatusDetailDisplay => !IsEnabled
        ? "Rule is off and Roblox keeps the original audio."
        : !HasSourceReference(FilePath)
            ? "Add a local file, direct URL, or 6-letter code."
            : IsPrepared
                ? "Prepared and ready to replace matching cache audio."
                : "Save and apply this rule to prepare it.";

    [JsonIgnore]
    public string VisualState => !IsEnabled
        ? "Inactive"
        : IsReplacementPresentInCache
            ? "Active"
            : IsOriginalPresentInCache
                ? "Ready"
                : "Inactive";

    [JsonIgnore]
    public string VisualDotState => !IsEnabled
        ? "Muted"
        : IsReplacementPresentInCache
            ? "Active"
            : IsOriginalPresentInCache
                ? "Ready"
                : HasSourceReference(FilePath)
                    ? "Standby"
                    : "Muted";

    public static int LatestPreparationVersion => CurrentPreparationVersion;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        NotifyDerivedProperties(propertyName);
    }

    private static string FormatKilobytes(long bytes) => bytes <= 0 ? "-" : $"{bytes / 1024d:N1} KB";

    private static bool LooksLikeSongCode(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var trimmed = source.Trim();
        return trimmed.Length == 6 && trimmed.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
    }

    private static bool HasSourceReference(string? source)
    {
        return !string.IsNullOrWhiteSpace(source);
    }

    private static bool IsRemoteSource(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static string TryGetRemoteDisplayName(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileName(uri.AbsolutePath);
            return string.IsNullOrWhiteSpace(name) ? uri.Host : name;
        }

        return Path.GetFileName(source);
    }

    private void NotifyDerivedProperties(string? propertyName)
    {
        if (string.Equals(propertyName, nameof(IsEnabled), StringComparison.Ordinal)
            || string.Equals(propertyName, nameof(SourceAssetHash), StringComparison.Ordinal)
            || string.Equals(propertyName, nameof(ReplacementFileHash), StringComparison.Ordinal)
            || string.Equals(propertyName, nameof(SourceAssetLength), StringComparison.Ordinal)
            || string.Equals(propertyName, nameof(ReplacementFileLength), StringComparison.Ordinal)
            || string.Equals(propertyName, nameof(PreparedAt), StringComparison.Ordinal)
            || string.Equals(propertyName, nameof(PreparationVersion), StringComparison.Ordinal)
            || string.Equals(propertyName, nameof(ReplacementSourceWasConverted), StringComparison.Ordinal)
            || string.Equals(propertyName, nameof(IsOriginalPresentInCache), StringComparison.Ordinal)
            || string.Equals(propertyName, nameof(IsReplacementPresentInCache), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsPrepared));
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(StatusTone));
            OnPropertyChanged(nameof(StatusDetailDisplay));
            OnPropertyChanged(nameof(VisualState));
            OnPropertyChanged(nameof(VisualDotState));
        }

        if (string.Equals(propertyName, nameof(FilePath), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(FileNameDisplay));
            OnPropertyChanged(nameof(SourceTypeDisplay));
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(StatusTone));
            OnPropertyChanged(nameof(StatusDetailDisplay));
            OnPropertyChanged(nameof(VisualDotState));
        }

        if (string.Equals(propertyName, nameof(ReplacementSourceWasConverted), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ReplacementSourceNoteDisplay));
        }
    }
}
