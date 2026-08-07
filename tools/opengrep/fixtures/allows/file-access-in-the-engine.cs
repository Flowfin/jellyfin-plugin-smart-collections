// The near miss for file-access-in-the-engine. Fires nothing.
//
// The bytes arrive as an argument. Whoever read them owns the directory, the
// permission and the failure, and the engine is handed a value a test can
// produce without touching a disk.
//
// The last member is the near miss the rule has to survive: a type whose name
// begins with a refused token and continues into another word is a different
// type, and a rule matching the prefix would refuse it.

internal static class ReadingEngine
{
    public static string Load(byte[] content)
    {
        return System.Text.Encoding.UTF8.GetString(content);
    }

    public static bool Accepts(FileFormat format)
    {
        return format == FileFormat.Json;
    }
}
