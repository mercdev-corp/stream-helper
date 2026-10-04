namespace StreamHelper.Shared.UI;

public record MicComboItem(string? Id, string DisplayName, bool IsMissing)
{
    public override string ToString() => DisplayName;
}
