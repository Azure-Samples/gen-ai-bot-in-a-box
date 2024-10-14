class MimeType
{
    private static Dictionary<string, string> types = new() {
        { "c", "text/x-c" },
        { "cpp", "text/x-c++" },
        { "cs", "text/x-csharp" },
        { "css", "text/css" },
        { "doc", "application/msword" },
        { "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "gif", "image/gif" },
        { "go", "text/x-golang" },
        { "html ","text/html" },
        { "java ","text/x-java" },
        { "jpg", "image/jpeg" },
        { "jpeg", "image/jpeg" },
        { "js", "text/javascript" },
        { "json ","application/json" },
        { "md", "text/markdown" },
        { "pdf", "application/pdf" },
        { "php", "text/x-php" },
        { "png", "image/png" },
        { "pptx" ,"application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { "py", "text/x-python" },
        { "rb", "text/x-ruby" },
        { "sh", "application/x-sh" },
        { "tex", "text/x-tex" },
        { "ts", "application/typescript" },
        { "txt", "text/plain" },
        { "webp", "image/webp" },
        { "xls" ,"application/vnd.ms-excel" },
        { "xlsx" ,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
    };

    public static string FromFilename(string filename)
    {
        var ext = filename.Split('.').Last();
        return types[ext] ?? "application/octet-stream";
    }
}