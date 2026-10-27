using Homestay.BLL.Infrastructure;

namespace Homestay.GUI.Infrastructure;

public static class ErrorHandler
{
    public static FileLogger? Logger { get; set; }
    public static event Action? SessionExpired;
    public static string MessageFor(Exception exception) => exception switch
    {
        NotImplementedException => "Chức năng đang làm. Vui lòng thử lại sau.",
        BusinessException => exception.Message,
        InvalidDataException or ArgumentException => exception.Message,
        UnauthorizedAccessException => "Bạn không có quyền thực hiện thao tác này.",
        _ => $"Có lỗi xảy ra (mã lỗi: {Logger?.Error(exception) ?? "CHUA-CO-LOG"}). Vui lòng báo quản lý."
    };
    public static void Show(Exception exception, IWin32Window? owner = null)
    {
        if (exception is OperationCanceledException) return;
        MessageBox.Show(owner, MessageFor(exception), "Fuji Homestay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (exception is SessionExpiredException) SessionExpired?.Invoke();
    }
    public static async Task RunAsync(Control trigger, Func<Task> action, Func<Task>? reload = null, Func<bool>? enabledAfter = null)
    {
        if (!trigger.Enabled) return;
        trigger.Enabled = false;
        try { await action(); }
        catch (ConcurrencyException exception)
        {
            if (trigger.IsDisposed) return;
            if (reload is not null)
            {
                try { await reload(); }
                catch (Exception loadError) { Show(loadError, trigger.FindForm()); return; }
            }
            Show(exception, trigger.FindForm());
        }
        catch (Exception exception) { if (!trigger.IsDisposed) Show(exception, trigger.FindForm()); }
        finally { if (!trigger.IsDisposed) trigger.Enabled = enabledAfter?.Invoke() ?? true; }
    }
    public static void Pending(IWin32Window? owner = null) => Show(new NotImplementedException(), owner);
}
