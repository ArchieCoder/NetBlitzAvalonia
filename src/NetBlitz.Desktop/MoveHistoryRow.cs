namespace NetBlitz.Desktop;

public sealed record MoveHistoryRow(int Number, string White, string Black)
{
    public string Label => $"{Number}.";
}
