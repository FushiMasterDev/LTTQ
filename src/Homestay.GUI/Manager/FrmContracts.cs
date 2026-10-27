using Homestay.GUI.Common;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Manager;

public partial class FrmContracts : ScreenForm
{
    public FrmContracts()
    {
        InitializeComponent();
        ListingDraftPreview.Add(toolbar,("Biểu mẫu hợp đồng",()=>ListingDraftEditors.Contract([],EmployeeUi.Today)));
        HelpText = "Mỗi homestay có tối đa một hợp đồng Active. Hoa hồng 10–15%; hệ thống kiểm thời gian và điều kiện. Chức năng đang được hoàn thiện.";
    }
}
