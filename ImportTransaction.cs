#nullable enable
using System;
using System.IO;
namespace StructureHandler;

internal sealed class ImportRecoveryException : AggregateException
{
    internal const string RecoveryMessage = "Restoring the original tile failed. Reload your last good save without saving this session.";
    internal ImportRecoveryException(Exception failure, Exception recovery) : base(RecoveryMessage, failure, recovery) { }
}

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
                throw new ImportRecoveryException(failure, recovery);
            }
            throw new InvalidDataException(
                $"Import of '{name}' failed. The original objects were restored.", failure);
        }
    }
}
