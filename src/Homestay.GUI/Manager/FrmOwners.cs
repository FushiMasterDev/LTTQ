using Homestay.GUI.Common;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Manager;

public partial class FrmOwners : ScreenForm
{
    public FrmOwners()
    {
        InitializeComponent();
        ListingDraftPreview.Add(toolbar,("Biểu mẫu chủ",()=>ListingDraftEditors.Owner()));
        HelpText = "Chủ không có tài khoản đăng nhập. Chỉ cập nhật thông tin ngân hàng sau khi gọi xác minh. Chức năng đang được hoàn thiện.";
    }
}
