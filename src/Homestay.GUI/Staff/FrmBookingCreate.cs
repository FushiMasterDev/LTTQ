using Homestay.GUI.Common;

namespace Homestay.GUI.Staff;

public partial class FrmBookingCreate : ScreenForm
{
    public FrmBookingCreate()
    {
        InitializeComponent();
        HelpText = "Chọn khách và phòng, xem báo giá từng đêm rồi tạo đơn. Cọc và thời hạn do BLL trả về. Không có voucher ở giai đoạn này. Chưa tạo đơn / chạy đồng hồ khi chưa có dữ liệu thật.";
    }
}
