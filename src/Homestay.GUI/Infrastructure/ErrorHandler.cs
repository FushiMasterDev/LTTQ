namespace Homestay.GUI.Infrastructure;

public static class ErrorHandler
{
    public static FileLogger? Logger { get; set; }
    public static void Show(Exception exception, IWin32Window? owner = null)
    {
        if (exception is OperationCanceledException) return;
        var message = exception switch
        {
            NotImplementedException => "Chức năng đang làm. Đang chờ hợp đồng BLL/DTO từ phía MAC.",
            InvalidDataException or ArgumentException => exception.Message,
            UnauthorizedAccessException => "Bạn không có quyền thực hiện thao tác này.",
            _ => $"Có lỗi xảy ra (mã lỗi: {Logger?.Error(exception) ?? "CHUA-CO-LOG"}). Vui lòng báo quản lý."
        };
        MessageBox.Show(owner, message, "Fuji Homestay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    public static async Task RunAsync(Control trigger, Func<Task> action)
    {
        if (!trigger.Enabled) return;
        trigger.Enabled = false;
        try { await action(); }
        catch (Exception exception) { Show(exception, trigger.FindForm()); }
        finally { if (!trigger.IsDisposed) trigger.Enabled = true; }
    }
    public static void Pending(IWin32Window? owner = null) => Show(new NotImplementedException(), owner);
}
