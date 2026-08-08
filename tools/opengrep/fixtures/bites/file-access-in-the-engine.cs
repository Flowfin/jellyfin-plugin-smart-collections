// Fires file-access-in-the-engine, and nothing else.
//
// The engine reading a document off disk puts the file system inside the part of
// the plugin that is supposed to answer the same way for the same inputs. The
// same compiled rule then depends on what is on a disk nothing declared, and a
// test that wants to exercise it needs a directory and a permission instead of a
// value.

internal static class LoadingEngine
{
    public static string Load(string path)
    {
        return File.ReadAllText(path);
    }
}
