using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

public partial class FrmContracts : ScreenForm
{
    public FrmContracts()
    {
        InitializeComponent();
        HelpText = "Mỗi homestay có tối đa một hợp đồng Active. Hoa hồng 10–15%; BLL kiểm thời gian và điều kiện. Chưa nối ContractService.";
    }
}
