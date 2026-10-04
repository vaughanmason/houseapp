namespace HomeInventory.Client.Components;

/// <summary>A destructive action waiting for the user to confirm it in a <see cref="ConfirmDialog"/>.</summary>
public sealed record PendingConfirmation(string Title, string Message, Func<Task> Action)
{
    /// <summary>Runs <paramref name="pending"/> (if any) after the dialog has been dismissed by clearing the field.</summary>
    public static Task RunAsync(PendingConfirmation? pending) => pending?.Action() ?? Task.CompletedTask;
}
