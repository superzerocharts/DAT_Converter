namespace DatConverter;

public sealed class FolderImportReviewContent
{
    public string Title { get; init; } = "";

    public string Text { get; init; } = "";

    public bool ShowsCombineQuestion { get; init; }

    public static FolderImportReviewChoice MapYes(bool showsCombineQuestion)
    {
        return showsCombineQuestion
            ? FolderImportReviewChoice.UseRecommendedImport
            : FolderImportReviewChoice.ImportEveryDatSeparately;
    }
}

public enum FolderImportReviewChoice
{
    Cancel,
    UseRecommendedImport,
    ImportEveryDatSeparately
}
