using Homestay.GUI.Common;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Manager;

public partial class FrmHomestays : ScreenForm
{
    public FrmHomestays()
    {
        InitializeComponent();
        ListingDraftPreview.Add(toolbar,("Biểu mẫu homestay",()=>ListingDraftEditors.Homestay([],[])));
        HelpText = "NV chỉ sửa hồ sơ nháp theo quyền hệ thống; tab Chờ duyệt dành cho QL. Chức năng đang được hoàn thiện.";
    }
}
