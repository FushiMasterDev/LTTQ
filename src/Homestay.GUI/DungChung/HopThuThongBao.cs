using System.ComponentModel;
using Homestay.DTO.Common;
using Homestay.DTO.Notifications;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Common;

/// <summary>Hộp thư thông báo trên màn hình chính: đếm chưa đọc, xem, bấm để mở đơn/phiếu liên quan.</summary>
public sealed class NotificationInbox : UserControl
{
    private const int PageSize=20;
    private readonly ListBox list=new(){Dock=DockStyle.Fill,IntegralHeight=false,DrawMode=DrawMode.OwnerDrawVariable};
    private readonly Label status=new(){Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(6)};
    private readonly Button previous=new(){Text="Trước",AutoSize=true};
    private readonly Button next=new(){Text="Sau",AutoSize=true};
    private readonly Button readAll=new(){Text="Đọc tất cả",AutoSize=true};
    private readonly Button reload=new(){Text="Tải lại",AutoSize=true};
    private readonly CancellationTokenSource lifetime=new();
    private readonly Font unreadFont;
    private Func<PageRequest,CancellationToken,Task<PagedResult<NotificationItem>>>? readPage;
    private Func<CancellationToken,Task<int>>? countUnread;
    private Func<long,CancellationToken,Task>? markRead;
    private Func<CancellationToken,Task>? markAll;
    private bool refreshing,writing,pending,disposed;
    private int page=1,total,unread;
    public event Action<int>? UnreadCountChanged;
    public event Action<int>? BookingRequested;
    public event Action<int>? PaymentRequested;

    [Browsable(false),DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CurrentPage=>page;
    [Browsable(false),DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int UnreadCount=>unread;

    public NotificationInbox(){
        unreadFont=new Font(list.Font,FontStyle.Bold);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,WrapContents=true,Padding=new Padding(4)};
        buttons.Controls.AddRange([previous,next,readAll,reload]);Controls.Add(list);Controls.Add(status);Controls.Add(buttons);
        status.Text="Thông báo";list.DisplayMember=nameof(NotificationItem.Message);
        list.AccessibleName="Danh sách thông báo";
        list.MeasureItem+=(_,e)=>{
            if(e.Index<0||e.Index>=list.Items.Count)return;
            var item=(NotificationItem)list.Items[e.Index];
            var size=TextRenderer.MeasureText(RowText(item),item.IsRead?list.Font:unreadFont,
                new Size(Math.Max(80,list.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-16),0),
                TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            e.ItemHeight=Math.Clamp(size.Height+16,48,220);
        };
        list.DrawItem+=(_,e)=>{
            if(e.Index<0||e.Index>=list.Items.Count)return;
            var item=(NotificationItem)list.Items[e.Index];e.DrawBackground();
            TextRenderer.DrawText(e.Graphics,RowText(item),item.IsRead?list.Font:unreadFont,
                Rectangle.Inflate(e.Bounds,-6,-6),e.ForeColor,
                TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
        previous.Click+=async(_,_)=>{if(!refreshing&&!writing&&page>1){page--;await RefreshAsync();}};
        next.Click+=async(_,_)=>{if(!refreshing&&!writing&&page*PageSize<total){page++;await RefreshAsync();}};
        reload.Click+=async(_,_)=>await RefreshAsync();
        readAll.Click+=async(_,_)=>await WriteAsync(async()=>{
            await markAll!(lifetime.Token);page=1;
        });
        list.DoubleClick+=async(_,_)=>await OpenSelectedAsync();
        list.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await OpenSelectedAsync();}};
        UpdateButtons();
    }
    public NotificationInbox(Func<PageRequest,CancellationToken,Task<PagedResult<NotificationItem>>> readPage,
        Func<CancellationToken,Task<int>> countUnread,Func<long,CancellationToken,Task> markRead,
        Func<CancellationToken,Task> markAll):this(){
        this.readPage=readPage;this.countUnread=countUnread;this.markRead=markRead;this.markAll=markAll;
    }
    private static string RowText(NotificationItem item)=>
        (item.IsRead?"Đã đọc":"MỚI")+" · "+UiFormat.VietnamTime(new DateTimeOffset(DateTime.SpecifyKind(item.CreatedAtUtc,DateTimeKind.Utc))).ToString("dd/MM/yyyy HH:mm")+"\n"+item.Message;
    public async Task RefreshAsync(){
        if(disposed||readPage is null||countUnread is null)return;
        if(refreshing||writing){pending=true;return;}
        refreshing=true;UpdateButtons();
        try{
            do{
                pending=false;
                var selected=(list.SelectedItem as NotificationItem)?.Id;
                var nextUnread=await countUnread(lifetime.Token);
                var result=await readPage(new(){PageNumber=page,PageSize=PageSize},lifetime.Token);
                var max=Math.Max(1,(int)Math.Ceiling((double)result.TotalCount/PageSize));
                if(page>max){page=max;result=await readPage(new(){PageNumber=page,PageSize=PageSize},lifetime.Token);}
                if(disposed)return;
                total=result.TotalCount;unread=nextUnread;
                var items=result.Items.ToArray();list.DataSource=items;list.ClearSelected();
                if(selected is long id){var index=Array.FindIndex(items,x=>x.Id==id);if(index>=0)list.SelectedIndex=index;}
                status.Text="Trang "+page+"/"+max+" · "+total+" thông báo · "+unread+" chưa đọc";
                UnreadCountChanged?.Invoke(unread);
            }while(pending&&!disposed);
        }catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception e){if(!disposed){status.Text="Không tải được thông báo. Bấm Tải lại.";ErrorHandler.Logger?.Error(e);}}
        finally{refreshing=false;if(!disposed)UpdateButtons();}
    }
    private async Task OpenSelectedAsync(){
        if(list.SelectedItem is not NotificationItem item||refreshing||writing)return;
        await WriteAsync(async()=>{
            if(!item.IsRead)await markRead!(item.Id,lifetime.Token);
            if(disposed)return;
            if(item.RefType is "Booking" or "Bookings"&&int.TryParse(item.RefId,out var id))BookingRequested?.Invoke(id);
            else if(item.RefType is "Payment" or "Payments"&&int.TryParse(item.RefId,out var paymentId)&&PaymentRequested is not null)PaymentRequested.Invoke(paymentId);
            else MessageBox.Show(FindForm(),item.Message,"Thông báo",MessageBoxButtons.OK,MessageBoxIcon.Information);
        });
    }
    private async Task WriteAsync(Func<Task> action){
        if(disposed||writing||refreshing||markRead is null||markAll is null)return;
        writing=true;UpdateButtons();
        try{await action();}
        catch(Exception e){if(!disposed)ErrorHandler.Show(e,FindForm());}
        finally{writing=false;if(!disposed){await RefreshAsync();UpdateButtons();}}
    }
    private void UpdateButtons(){
        var ready=!refreshing&&!writing&&readPage is not null;
        previous.Enabled=ready&&page>1;next.Enabled=ready&&page*PageSize<total;
        readAll.Enabled=ready&&unread>0;reload.Enabled=ready;list.Enabled=ready;
    }
    protected override void Dispose(bool disposing){
        if(disposing&&!disposed){disposed=true;lifetime.Cancel();lifetime.Dispose();unreadFont.Dispose();}
        base.Dispose(disposing);
    }
}
