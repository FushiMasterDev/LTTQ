using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

public partial class FrmCatalog : ScreenForm
{
    public FrmCatalog()
    {
        InitializeComponent();
        HelpText = "Không xóa danh mục đã dùng; chuyển Ngừng sử dụng. Chính sách hủy tạo phiên bản mới để không ảnh hưởng đơn cũ. Chưa nối CatalogService.";
    }
}
