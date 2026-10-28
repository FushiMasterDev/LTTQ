namespace Homestay.GUI.Workflow;

/// <summary>Theo dõi chuột/bàn phím để biết người dùng bỏ máy bao lâu, phục vụ tự khóa màn hình.</summary>
public sealed class ActivityTracker : IMessageFilter
{
    private readonly Func<long> clock;
    private long lastActivity;
    public ActivityTracker(Func<long>? clock = null)
    { this.clock=clock ?? (()=>Environment.TickCount64);Touch(); }
    public TimeSpan IdleFor => TimeSpan.FromMilliseconds(Math.Max(0,clock()-lastActivity));
    public void Touch() => lastActivity=clock();
    public bool PreFilterMessage(ref Message message)
    {
        if (message.Msg is 0x100 or 0x104 or 0x200 or 0x201 or 0x204 or 0x207 or 0x20A)
            Touch();
        return false;
    }
}
