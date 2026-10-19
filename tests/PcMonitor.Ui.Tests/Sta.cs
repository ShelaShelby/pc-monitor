using System.Runtime.ExceptionServices;

namespace PcMonitor.Ui.Tests;

internal static class Sta
{
    /// <summary>Runs <paramref name="action"/> on a fresh STA thread, as WPF expects, and rethrows its failure.</summary>
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
