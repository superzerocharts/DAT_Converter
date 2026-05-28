namespace DatConverter;

public static class FolderImportReviewContentBuilder
{
    public static FolderImportReviewContent Build(FolderImportPlan importPlan)
    {
        if (importPlan.SplitRecordingCount > 0)
        {
            var recordingText = importPlan.SplitRecordingCount == 1
                ? "1 multi-part recording"
                : $"{importPlan.SplitRecordingCount} multi-part recordings";
            return new FolderImportReviewContent
            {
                Title = "Combine detected recording parts?",
                ShowsCombineQuestion = true,
                Text =
                    $"DAT Converter found {recordingText} in this folder/subfolders.\r\n\r\n" +
                    "Yes: combine each verified recording into one video.\r\n" +
                    "No: add every DAT file separately.\r\n\r\n" +
                    "Files from different recordings will not be combined together.\r\n" +
                    "Unverified DAT files will be added separately."
            };
        }

        var datText = importPlan.AllDatPaths.Count == 1
            ? "1 DAT file"
            : $"{importPlan.AllDatPaths.Count} DAT files";
        return new FolderImportReviewContent
        {
            Title = "Add DAT files?",
            ShowsCombineQuestion = false,
            Text = $"DAT Converter found {datText}."
        };
    }
}
