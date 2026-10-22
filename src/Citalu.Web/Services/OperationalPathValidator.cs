namespace Citalu.Web.Services;

internal static class OperationalPathValidator
{
    // Acepta archivos de captura fuera del árbol servido y rechaza rutas dentro de wwwroot.
    internal static bool IsInsideWebRoot(string webRoot, string candidatePath)
    {
        var fullWebRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(webRoot));
        var fullCandidatePath = Path.GetFullPath(candidatePath);
        var relativePath = Path.GetRelativePath(fullWebRoot, fullCandidatePath);

        return relativePath == "." ||
            (!Path.IsPathRooted(relativePath) && relativePath != ".." &&
             !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
}
