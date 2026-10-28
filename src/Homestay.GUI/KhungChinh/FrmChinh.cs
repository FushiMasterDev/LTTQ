using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Notifications;
using Homestay.DTO.Enums;
using Homestay.GUI.Staff;
using Homestay.BLL.Infrastructure;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Workflow;

namespace Homestay.GUI.Shell;

/// <summary>Màn hình chính sau khi đăng nhập: menu theo chức vụ, vùng chứa các màn hình con, thông báo, đăng xuất.</summary>
public partial class FrmMain : ScreenForm
{
    private readonly Dictionary<string, TabPage> tabs = [];
    private readonly System.Windows.Forms.Timer heartbeatTimer;
    private readonly System.Windows.Forms.Timer idleTimer;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ActivityTracker activity = new();
    private IAccountGateway? gateway;
    private AppServices? services;
    private int idleMinutes = 30;
    private string connectedStatus = "";
    private bool changingRole, ending, allowClose, heartbeatBusy, locked, runtimeDisposed;
    public event EventHandler? SignedOut;

    public FrmMain()
    {
        InitializeComponent();
        heartbeatTimer = new System.Windows.Forms.Timer(components!) { Interval = 60_000 };
        idleTimer = new System.Windows.Forms.Timer(components!) { Interval = 5_000 };
        InitializeNavigation();
        roleBox.SelectedIndexChanged += (_, _) => { if (gateway is null) ChangePreviewRole(); };
        notificationsButton.Click += (_, _) => notificationSplit.Panel2Collapsed = !notificationSplit.Panel2Collapsed;
        closeTabButton.Click += (_, _) => CloseCurrentTab();
        exitButton.Click += async (_, _) => await EndSessionAsync(false);
        logoutButton.Click += async (_, _) => await EndSessionAsync(true);
        lockButton.Click += async (_, _) => await LockScreenAsync();
        generalHelpButton.Click += (_, _) => MessageBox.Show(this,
            "Chọn chức năng bên trái. F5 tải lại; Ctrl+F tìm; Esc đóng tab; Ctrl+Enter lưu hộp thoại.\n" +
            "Chức năng hiện theo vai trò. Không chia sẻ tài khoản đăng nhập.", "Hướng dẫn");
        quickSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if(services is not null) _=SearchQuickAsync(); else ErrorHandler.Pending(this); }
        };
        heartbeatTimer.Tick += async (_, _) => await HeartbeatAsync();
        idleTimer.Tick += async (_, _) =>
        {
            if (gateway is not null && !ending && !locked && activity.IdleFor >= TimeSpan.FromMinutes(idleMinutes))
                await LockScreenAsync();
        };
    }
    public FrmMain(IAccountGateway gateway, AppServices? services = null, int idleMinutes = 30) : this()
    {
        this.gateway = gateway; this.services = services; this.idleMinutes = idleMinutes;
        var session = gateway.Session ?? throw new SessionExpiredException();
        if (session.MustChangePassword) throw new SessionExpiredException("Cần đổi mật khẩu trước khi tiếp tục.");
        roleBox.Visible = false; quickSearch.Visible = services is not null; notificationsButton.Visible = services is not null;
        logoutButton.Visible = true; lockButton.Visible = true; userLabel.Visible = true;
        userLabel.Text = session.FullName + " · " + UiNames.Position(session.Position);
        banner.Visible = false;
        Text = "Fuji Homestay · " + UiNames.Position(session.Position);
        connectedStatus = sessionStatus.Text = $"{session.FullName} · {UiNames.Position(session.Position)} · Khu vực: " +
            (session.HasAllAreas ? "Toàn hệ thống" : session.AreaIds.Count == 0 ? "Chưa được gán" : string.Join(", ",session.AreaIds)) +
            $" · Đăng nhập: {UiFormat.VietnamTime(new DateTimeOffset(DateTime.SpecifyKind(session.LoginAtUtc,DateTimeKind.Utc))):HH:mm dd/MM/yyyy} · Đã kết nối · 0.2.0";
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (DesignMode || System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime) return;
        if (gateway is null) roleBox.SelectedIndex = 0;
        else
        {
            SetMenu(ScreenRegistry.Production(gateway, services).Where(e=>e.Id is not ("F09" or "F10" or "F11" or "F16")).ToArray());
            var welcome = new TabPage("Trang chủ");
            welcome.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(32),
                Text = "Chào " + gateway.Session!.FullName + ".\n\nChọn chức năng ở menu bên trái để làm việc.",
                Font = new Font("Segoe UI", 14), ForeColor = UiTheme.Ink
            });
            workspaceTabs.TabPages.Add(welcome); tabs["home"] = welcome;
            ErrorHandler.SessionExpired += HandleSessionExpired;
            System.Windows.Forms.Application.AddMessageFilter(activity);
            heartbeatTimer.Start(); idleTimer.Start();
            if(services is not null) {
                services.NavigationRequested+=OpenRequested;
                SetupNotifications();
                _=RefreshNotificationsAsync();
                // Only start maintenance while this signed-in shell is alive.
                _=RunJobsAsync();
            }
        }
    }
    private void ChangePreviewRole()
    {
        foreach(var page in tabs.Values.ToArray()) { workspaceTabs.TabPages.Remove(page); page.Dispose(); }
        tabs.Clear();
        SetMenu(ScreenRegistry.Layouts.Where(s => ScreenRegistry.IsPreviewAudience(s, roleBox.Text)).OrderBy(s=>s.Id).ToArray());
        var dashboard = ScreenRegistry.Layouts.First(s => s.Id == "F05");
        OpenScreen(dashboard.Id, dashboard.Create);
    }
    public void OpenScreen(string key, Func<Form> create)
    {
        if (gateway is not null && !(key.Split(':')[0]=="F31"&&gateway.Can(Permission.SettlementView)) && !ScreenRegistry.Production(gateway,services).Any(s=>s.Id==key.Split(':')[0]))
            throw new ForbiddenException();
        if (tabs.TryGetValue(key, out var existing)) { workspaceTabs.SelectedTab = existing; return; }
        var form = create();
        var page = new TabPage(form.Text) { Tag = key };
        form.TopLevel = false; form.FormBorderStyle = FormBorderStyle.None;
        form.Dock = DockStyle.Fill; form.MinimumSize = Size.Empty;
        page.Controls.Add(form);
        tabs.Add(key, page); workspaceTabs.TabPages.Add(page);
        form.FormClosed += (_, _) =>
        {
            tabs.Remove(key); workspaceTabs.TabPages.Remove(page);
            if (!page.IsDisposed) page.Dispose();
        };
        form.Show(); workspaceTabs.SelectedTab = page;
    }
    private void CloseCurrentTab()
    {
        var page = workspaceTabs.SelectedTab;
        if (page?.Controls.OfType<Form>().FirstOrDefault() is Form form) form.Close();
        else if (page is not null) { tabs.Remove("home"); workspaceTabs.TabPages.Remove(page); page.Dispose(); }
    }
    private async Task HeartbeatAsync()
    {
        if (gateway is null || heartbeatBusy || ending) return;
        heartbeatBusy = true;
        try
        {
            if (!await gateway.HeartbeatAsync(lifetime.Token)) await EndSessionAsync(true, forced:true);
            else if (!IsDisposed) { sessionStatus.ForeColor = Color.DarkGreen; sessionStatus.Text = connectedStatus; await RefreshNotificationsAsync(); await RunJobsAsync(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!IsDisposed && !ending)
            {
                sessionStatus.ForeColor = Color.Firebrick;
                sessionStatus.Text = "Chưa kiểm tra được kết nối. Các thao tác lưu vẫn được kiểm tra phiên tại máy chủ.";
                ErrorHandler.Logger?.Error(exception);
            }
        }
        finally { heartbeatBusy = false; }
    }
    private async Task LockScreenAsync()
    {
        if (gateway is null || ending || locked) return;
        locked = true; root.Enabled = false;
        try
        {
            using var dialog = new FrmLogin(gateway, unlock:true);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                await EndSessionAsync(true, forced:true);
            else activity.Touch();
        }
        catch (SessionExpiredException) { await EndSessionAsync(true, forced:true); }
        catch (Exception exception) { ErrorHandler.Show(exception,this); }
        finally { locked = false; if (!IsDisposed && !ending) root.Enabled = true; }
    }
    private async void HandleSessionExpired()
    {
        if (!ending && !IsDisposed) await EndSessionAsync(true, forced:true);
    }
    private async Task EndSessionAsync(bool returnToLogin, bool forced = false)
    {
        if (ending || IsDisposed) return;
        if (!forced && MessageBox.Show(this,
            gateway is null ? "Kết thúc xem bố cục?" : returnToLogin ? "Đăng xuất tài khoản?" : "Thoát ứng dụng?",
            "Fuji Homestay",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
        ending = true; root.Enabled = false; heartbeatTimer.Stop(); idleTimer.Stop();
        try
        {
            if (gateway is not null)
            {
                try { await gateway.LogoutAsync(lifetime.Token); }
                catch (Exception exception)
                {
                    if (!forced) { ErrorHandler.Show(exception,this); ending=false; root.Enabled=true; heartbeatTimer.Start();idleTimer.Start();return; }
                    ErrorHandler.Logger?.Error(exception);
                }
            }
            allowClose=true;
            if (returnToLogin && gateway is not null) SignedOut?.Invoke(this,EventArgs.Empty);
            Close();
        }
        finally { if (!IsDisposed && !allowClose) { ending=false;root.Enabled=true; } }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && services is not null && quickSearch.Focused)
        { _ = SearchQuickAsync(); return true; }
        if (keyData == (Keys.Control | Keys.N) && services is not null && gateway?.Can(Permission.BookingCreate)==true)
        { OpenRequested("F07",null); return true; }
        if (keyData == (Keys.Control | Keys.K)) { quickSearch.Focus(); return true; }
        if (keyData == (Keys.Control | Keys.N) && gateway is null && roleBox.Text != "Kế toán")
        { var screen=ScreenRegistry.Layouts.First(s=>s.Id=="F07");OpenScreen(screen.Id,screen.Create);return true; }
        if (keyData == Keys.Escape) { CloseCurrentTab();return true; }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose && e.CloseReason == CloseReason.UserClosing)
        { e.Cancel=true; _=EndSessionAsync(false); }
        base.OnFormClosing(e);
    }
    private bool jobsBusy;
    private NotificationInbox? inbox;
    private async Task RunJobsAsync() {
        if(services is null||jobsBusy||ending)return;jobsBusy=true;
        try { var n=await services.Jobs.RunOnceAsync(lifetime.Token); if(n>0)services.Logger.Maintenance(n); }
        catch(OperationCanceledException){} catch(Exception e){services.Logger.Error(e);}
        finally{jobsBusy=false;}
    }
    private void SetupNotifications() {
        notificationSplit.Panel2.Controls.Remove(notificationList);notificationList.Dispose();
        inbox=new NotificationInbox(
            (q,ct)=>services!.Notifications.ListAsync(q,ct),
            ct=>services!.Notifications.CountUnreadAsync(ct),
            (id,ct)=>services!.Notifications.MarkReadAsync(id,ct),
            ct=>services!.Notifications.MarkAllReadAsync(ct)){Dock=DockStyle.Fill};
        inbox.UnreadCountChanged+=count=>notificationsButton.Text="Thông báo ("+count+")";
        inbox.BookingRequested+=id=>services!.Navigate("F09",id);
        inbox.PaymentRequested+=_=>{if(services!.Can(Permission.BookingRefundRecord))services.Navigate("F29",null);};
        notificationSplit.Panel2.Controls.Add(inbox);
        notificationsButton.Click+=async(_,_)=>await RefreshNotificationsAsync();
    }
    private Task RefreshNotificationsAsync()=>ending||IsDisposed||inbox is null?Task.CompletedTask:inbox.RefreshAsync();
    private bool quickSearchBusy;
    private async Task SearchQuickAsync() {
        if(services is null||quickSearchBusy||ending||locked||string.IsNullOrWhiteSpace(quickSearch.Text))return;
        quickSearchBusy=true;
        try {
            var r=await services.Bookings.QuickSearchAsync(quickSearch.Text,lifetime.Token);
            if(IsDisposed||ending||locked)return;
            var choices=r.Bookings.Select(b=>new Choice("B:"+b.Id,b.Code+" · "+b.CustomerName+" · "+b.HomestayName))
                .Concat(r.Customers.Select(c=>new Choice("C:"+c.Id,c.FullName+" · "+c.Phone))).ToArray();
            if(choices.Length==0){MessageBox.Show(this,"Không tìm thấy kết quả.");return;}
            using var d=new FieldsDialog("Kết quả tìm nhanh",new FieldSpec("target","Đơn / khách",FieldKind.Choice,choices[0].Value,Choices:choices));
            if(d.ShowDialog(this)!=DialogResult.OK)return;var key=(string)d.Values["target"]!;
            if(key.StartsWith("B:"))OpenRequested("F09",int.Parse(key[2..]));
            else {var c=r.Customers.Single(c=>c.Id==int.Parse(key[2..]));OpenRequested("F13",c);}
        }catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
        finally{quickSearchBusy=false;}
    }
    private async void OpenRequested(string id,object? argument) {
        if(services is null||ending||IsDisposed)return;
        try {
            var key=id+(id is "F09" or "F10" or "F11" or "F16" or "F20" or "F31"?":"+argument:argument is BookingSearchRequest q?":"+q.Queue+":"+q.OnlyExpiringHolds+":"+q.OnlyOverdueConfirmation+":"+q.AssignedToMeOnly:"");
            if(id=="F13"&&argument is Homestay.DTO.Customers.CustomerItem c)key+=":"+c.Id;
            if(id=="F07"&&argument is Homestay.DTO.Availability.QuoteRequest r)key+=":"+r.RoomTypeId+":"+r.CheckIn+":"+r.CheckOut+":"+r.Guests;
            if(id=="F07"&&argument is BookingDraftSelection draft)key+=":calendar:"+draft.Quote.RoomTypeId+":"+draft.Quote.CheckIn+":"+draft.Quote.CheckOut+":"+draft.Quote.Guests+":"+draft.PreferredRoomId;
            if(id=="F07"&&argument is Homestay.DTO.Customers.CustomerItem customer)key+=":customer:"+customer.Id;
            if(argument is Homestay.GUI.Manager.ListingOwnerSelection owner)key+=":owner:"+owner.OwnerId;if(argument is BookingExceptionsSelection ex)key+=":exceptions:"+ex.Kind;if(argument is TaskBoardSelection task)key+=":tasks:"+task.BookingId+":"+task.OnlyOverdue;var alreadyOpen=tabs.ContainsKey(key);
            OpenScreen(key,()=>id switch {
                "F07"=>new FrmBookingCreate(services,argument),
                "F08"=>new FrmBookings(services,argument as BookingSearchRequest,argument as BookingExceptionsSelection),
                "F15"=>new Homestay.GUI.Manager.FrmHomestays(services,argument as Homestay.GUI.Manager.ListingOwnerSelection),"F19"=>new Homestay.GUI.Manager.FrmTasks(services,argument as TaskBoardSelection),"F27"=>new Homestay.GUI.Accountant.FrmDepositVerification(services),"F09"=>new FrmBookingDetail(services,(int)argument!),
                "F10"=>new FrmReceipt(services,(int)argument!),
                "F11"=>new FrmCancellation(services,(int)argument!),
                "F16"=>new Homestay.GUI.Manager.FrmHomestayProfile(services,(int)argument!),
                "F13"=>new FrmCustomers(services,argument as Homestay.DTO.Customers.CustomerItem),
                "F20"=>new Homestay.GUI.Manager.FrmCancellationApproval(services,argument as int?),
                "F29"=>new Homestay.GUI.Accountant.FrmRefundQueue(services),
                "F30"=>new Homestay.GUI.Accountant.FrmSettlements(services),
                "F31"=>new Homestay.GUI.Accountant.FrmSettlementDetail(services,(int)argument!),
                "F32"=>new Homestay.GUI.Accountant.FrmCashBook(services),
                _=>throw new NotImplementedException()
            });
            if(id=="F09"&&alreadyOpen&&tabs.TryGetValue(key,out var page)&&page.Controls.OfType<FrmBookingDetail>().FirstOrDefault() is {} detail)await detail.RefreshFromServerAsync();
            if(alreadyOpen&&tabs.TryGetValue(key,out var existing)){
                if(existing.Controls.OfType<Homestay.GUI.Manager.FrmHomestayProfile>().FirstOrDefault() is {} profile)await profile.RefreshFromServerAsync();
                if(existing.Controls.OfType<Homestay.GUI.Manager.FrmCancellationApproval>().FirstOrDefault() is {} approval)await approval.RefreshFromServerAsync();
                if(existing.Controls.OfType<Homestay.GUI.Accountant.FrmRefundQueue>().FirstOrDefault() is {} refunds)await refunds.RefreshFromServerAsync();
                if(existing.Controls.OfType<Homestay.GUI.Accountant.FrmSettlementDetail>().FirstOrDefault() is {} settlement)await settlement.RefreshFromServerAsync();
            }
        }catch(Exception e){ErrorHandler.Show(e,this);}
    }
    private void DisposeRuntime()
    {
        if (runtimeDisposed) return;
        runtimeDisposed = true;
        if(services is not null)services.NavigationRequested-=OpenRequested;
        ErrorHandler.SessionExpired -= HandleSessionExpired;
        System.Windows.Forms.Application.RemoveMessageFilter(activity);
        lifetime.Cancel(); lifetime.Dispose();
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    { DisposeRuntime(); base.OnFormClosed(e); }
}
