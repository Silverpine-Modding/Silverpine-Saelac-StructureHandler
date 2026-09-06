#nullable enable
using System;
using System.IO;
namespace StructureHandler;

internal static class ImportTransaction
{
    internal static void Execute(Action commit, Action rollback, string name)
    {
        try { commit(); }
        catch (Exception failure)
        {
            try { rollback(); }
            catch (Exception recovery)
            {
                throw new AggregateException(
                    "Import failed and rollback could not fully restore the tile. Reload your last game save.",
                    failure, recovery);
            }
            throw new InvalidDataException(
                $"Import of '{name}' failed. The original objects were restored.", failure);
        }
    }
}
