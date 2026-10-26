using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

public partial class FrmHomestays : ScreenForm
{
    public FrmHomestays()
    {
        InitializeComponent();
        HelpText = "NV chỉ sửa hồ sơ nháp theo quyền BLL; tab Chờ duyệt dành cho QL. Chưa nối HomestayService.";
    }
}
