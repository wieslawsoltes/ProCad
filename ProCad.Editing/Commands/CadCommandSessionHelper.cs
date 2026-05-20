using ProCad.Editing.Sessions;

namespace ProCad.Editing.Commands;

internal static class CadCommandSessionHelper
{
    public static bool TryGetSession(CadCommandContext context, out CadDocumentSession session, out CadCommandResult error)
    {
        if (context.Session is CadDocumentSession typed)
        {
            session = typed;
            error = CadCommandResult.Ok();
            return true;
        }

        session = null!;
        error = CadCommandResult.Fail("No active editable CAD document session.");
        return false;
    }
}
