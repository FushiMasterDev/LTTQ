using Homestay.GUI.Common;

namespace Homestay.GUI.Staff;

public partial class FrmBookingDetail : ScreenForm
{
    public FrmBookingDetail()
    {
        InitializeComponent();
        HelpText = "Chỉ cung cấp địa chỉ, SĐT cơ sở và hướng dẫn cho khách khi đơn đã xác nhận. Mọi nút cần kiểm trạng thái/quyền ở BLL. Kế toán chỉ đọc; chỉ QL/KT thấy hoa hồng. Bố cục đang hiển thị toàn bộ vùng để rà soát thiết kế.";
    }
}
