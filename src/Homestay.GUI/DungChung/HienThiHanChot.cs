namespace Homestay.GUI.Common;

// For display only. BLL remains responsible for expiry and permitted writes.
public static class DeadlineDisplay
{
    public static string Format(DateTime? deadlineUtc,DateTime serverNowUtc)
    {
        if(deadlineUtc is not {} deadline)return "";
        var absolute=UiFormat.VietnamTime(new DateTimeOffset(DateTime.SpecifyKind(deadline,DateTimeKind.Utc))).ToString("dd/MM/yyyy HH:mm");
        var remaining=deadline-serverNowUtc;
        if(remaining<=TimeSpan.Zero)return absolute+"\nĐã quá hạn";
        return absolute+"\nCòn "+(remaining.TotalDays>=1?((int)remaining.TotalDays)+" ngày ":"")+
            remaining.Hours.ToString("00")+":"+remaining.Minutes.ToString("00")+":"+remaining.Seconds.ToString("00");
    }
}
