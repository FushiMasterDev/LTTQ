using System.Net.Mail;
using Homestay.DTO.Common;
using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

// Input forms only. No database access, status transitions or business calculations.
// MAC contracts will supply lookups and map validated presentation values into requests.
public static class ListingDraftEditors
{
    private static Choice[] Choices(IEnumerable<Lookup> values)=>values.Select(v=>new Choice(v.Id,v.Name)).ToArray();
    public static FieldsDialog Owner(){
        var dialog=new FieldsDialog("Thông tin chủ homestay",
            new("name","Họ tên",MaxLength:100),new("phone","Số điện thoại",MaxLength:16),
            new("email","Email",Required:false,MaxLength:256),new("address","Địa chỉ",FieldKind.Multiline,Required:false,MaxLength:300),
            new("identity","CCCD / giấy tờ định danh",Required:false,MaxLength:20),
            new("bank","Ngân hàng",Required:false,MaxLength:100),new("account","Số tài khoản",Required:false,MaxLength:30),
            new("accountName","Tên chủ tài khoản",Required:false,MaxLength:100),new("note","Ghi chú",FieldKind.Multiline,Required:false,MaxLength:1000));
        dialog.SetSummary("Hồ sơ mới chờ duyệt. Thông tin ngân hàng cần đối chiếu với chủ trước khi cập nhật; chủ không có tài khoản đăng nhập.");
        dialog.ValidateValues=v=>v["email"] is string email&&!string.IsNullOrWhiteSpace(email)&&!MailAddress.TryCreate(email,out _)?"Email không hợp lệ. Vui lòng kiểm tra lại.":null;
        return dialog;
    }
    public static FieldsDialog Homestay(IReadOnlyList<Lookup> owners,IReadOnlyList<Lookup> areas){
        var dialog=new FieldsDialog("Thông tin homestay nháp",
            new("owner","Chủ homestay",FieldKind.Lookup,Choices:Choices(owners)),new("area","Khu vực",FieldKind.Lookup,Choices:Choices(areas)),
            new("name","Tên homestay",MaxLength:200),new("address","Địa chỉ",FieldKind.Multiline,MaxLength:300),
            new("phone","SĐT liên hệ",Required:false,MaxLength:16),
            new("checkin","Giờ nhận phòng",FieldKind.Time,new TimeOnly(14,0)),new("checkout","Giờ trả phòng",FieldKind.Time,new TimeOnly(12,0)),
            new("description","Mô tả",FieldKind.Multiline,Required:false,MaxLength:4000),new("guide","Hướng dẫn nhận phòng",FieldKind.Multiline,Required:false,MaxLength:2000));
        dialog.SetSummary("Tạo ở trạng thái Nháp. Sửa địa chỉ/khu vực của hồ sơ đang hoạt động có thể yêu cầu duyệt lại; dịch vụ kiểm tra quyền và điều kiện trước khi lưu.");
        return dialog;
    }
    public static FieldsDialog Contract(IReadOnlyList<Lookup> homestays,DateOnly today){
        var dialog=new FieldsDialog("Thông tin hợp đồng",
            new("homestay","Homestay",FieldKind.Lookup,Choices:Choices(homestays)),new("number","Số hợp đồng",MaxLength:30),
            new("start","Ngày bắt đầu",FieldKind.Date,today),new("end","Ngày kết thúc",FieldKind.Date,today.AddYears(1)),
            new("commissionPercent","Hoa hồng (%)",FieldKind.Decimal,10m,Minimum:10,Maximum:15));
        dialog.SetSummary("Hoa hồng nhập theo phần trăm, từ 10 đến 15%. Mỗi homestay tối đa một hợp đồng đang hiệu lực; dịch vụ kiểm tra trước khi lưu.");
        dialog.ValidateValues=v=>(DateOnly)v["end"]!<=(DateOnly)v["start"]!?"Ngày kết thúc phải sau ngày bắt đầu.":null;
        return dialog;
    }
    public static FieldsDialog RoomType(IReadOnlyList<Lookup> categories)=>new("Thông tin loại phòng",
        new("category","Danh mục loại phòng",FieldKind.Lookup,Choices:Choices(categories)),new("name","Tên loại phòng",MaxLength:100),
        new("guests","Số khách tối đa",FieldKind.Integer,1,Minimum:1,Maximum:int.MaxValue),
        new("area","Diện tích (m²)",FieldKind.Integer,Required:false,Minimum:1,Maximum:int.MaxValue),new("description","Mô tả",FieldKind.Multiline,Required:false,MaxLength:1000));
    public static FieldsDialog Room(IReadOnlyList<Lookup> roomTypes)=>new("Thông tin phòng",
        new("type","Loại phòng",FieldKind.Lookup,Choices:Choices(roomTypes)),new("code","Mã phòng",MaxLength:20),
        new("note","Ghi chú",FieldKind.Multiline,Required:false,MaxLength:255));
    public static FieldsDialog Price(IReadOnlyList<Lookup> roomTypes,DateOnly today){
        var dialog=new FieldsDialog("Phiên bản giá phòng",
            new("type","Loại phòng",FieldKind.Lookup,Choices:Choices(roomTypes)),new("effective","Ngày hiệu lực",FieldKind.Date,today),
            new("weekday","Giá ngày thường (₫)",FieldKind.Money,0m),new("weekend","Giá cuối tuần (₫)",FieldKind.Money,0m),new("holiday","Giá ngày lễ (₫)",FieldKind.Money,0m));
        dialog.SetSummary("Thêm phiên bản giá; không sửa giá đã hiệu lực. Ngày hiệu lực và quyền sửa sẽ được kiểm lại bằng giờ máy chủ. Đơn đã tạo giữ nguyên giá lúc đặt.");
        dialog.ValidateValues=v=>new[]{"weekday","weekend","holiday"}.Any(key=>(decimal)v[key]!<=0)?"Vui lòng nhập đủ ba mức giá lớn hơn 0.":null;
        return dialog;
    }
    public static FieldsDialog Block(IReadOnlyList<Lookup> rooms,DateOnly today){
        var dialog=new FieldsDialog("Khoảng khóa phòng",
            new("room","Phòng",FieldKind.Lookup,Choices:Choices(rooms)),new("start","Từ ngày",FieldKind.Date,today),new("end","Đến trước ngày",FieldKind.Date,today.AddDays(1)),
            new("reason","Lý do",FieldKind.Choice,"Maintenance",Choices:[new("Maintenance","Bảo trì"),new("OtherChannel","Khách từ kênh khác")]),
            new("note","Ghi chú",FieldKind.Multiline,Required:false,MaxLength:255));
        dialog.SetSummary("Chỉ Quản lý khóa/mở phòng. Ngày kết thúc không nằm trong khoảng khóa; dịch vụ kiểm tra trùng đơn và khóa khác trước khi lưu.");
        dialog.ValidateValues=v=>(DateOnly)v["end"]!<=(DateOnly)v["start"]!?"Ngày kết thúc khóa phải sau ngày bắt đầu.":null;
        return dialog;
    }
}

internal static class ListingDraftPreview
{
    public static void Add(FlowLayoutPanel toolbar,params (string Text,Func<FieldsDialog> Create)[] editors){
        foreach(var editor in editors){
            var button=new Button{Text=editor.Text,AutoSize=true,Tag="layout-editor",AccessibleDescription="Xem biểu mẫu đợt 4, chưa lưu dữ liệu"};
            button.Click+=(_,_)=>{using var dialog=editor.Create();dialog.SetPreviewMode();dialog.ShowDialog(toolbar.FindForm());};
            toolbar.Controls.Add(button);
        }
    }
}