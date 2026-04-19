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
        : TryGetRemoteDisplayName(FilePath);

    [JsonIgnore]
    public string ReplacementSourceNoteDisplay => ReplacementSourceWasConverted
        ? "Auto-converted to MP3"
        : string.Empty;

    [JsonIgnore]
    public string StatusDisplay => !IsEnabled
        ? "Original"
        : IsPrepared
            ? "Ready"
            : "Needs prep";

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
            || string.Equals(propertyName, nameof(ReplacementSourceWasConverted), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsPrepared));
            OnPropertyChanged(nameof(StatusDisplay));
        }

        if (string.Equals(propertyName, nameof(FilePath), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(FileNameDisplay));
        }

        if (string.Equals(propertyName, nameof(ReplacementSourceWasConverted), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ReplacementSourceNoteDisplay));
        }
    }
}
