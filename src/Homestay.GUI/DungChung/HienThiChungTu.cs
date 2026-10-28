using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DTO.Files;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Common;

public static class EvidencePresentation
{
    public const string FileFilter="Chứng từ (*.jpg;*.jpeg;*.png;*.webp;*.pdf)|*.jpg;*.jpeg;*.png;*.webp;*.pdf";
    public static async Task<EvidenceFile> ReadAsync(string path,CancellationToken token=default)
    {
        if(new FileInfo(path).Length>ReceiptRules.MaxEvidenceBytes)
            throw new BusinessException("Chứng từ tối đa 5 MB.");
        var bytes=await File.ReadAllBytesAsync(path,token);
        var validated=ReceiptRules.ValidateEvidence(bytes,Path.GetFileName(path))!;
        return new(Guid.Empty,validated.FileName,validated.ContentType,validated.Content);
    }
    public static string Extension(string contentType)=>contentType switch{
        "image/jpeg"=>".jpg","image/png"=>".png","image/webp"=>".webp","application/pdf"=>".pdf",
        _=>throw new BusinessException("Định dạng chứng từ không được hỗ trợ.")};
    public static void Show(IWin32Window owner,EvidenceFile file,string title)
    {
        var type=ReceiptRules.DetectContentType(file.Content);
        using var viewer=new Form{Text=title,Width=900,Height=680,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false};
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,Padding=new Padding(8),FlowDirection=FlowDirection.RightToLeft};
        var close=new Button{Text="Đóng",AutoSize=true,DialogResult=DialogResult.Cancel};
        var save=new Button{Text="Lưu chứng từ…",AutoSize=true};
        footer.Controls.AddRange([close,save]);
        var name=new Label{Text=file.FileName,AutoSize=true,Dock=DockStyle.Top,Padding=new Padding(8)};
        Image? picture=null;MemoryStream? stream=null;
        try{
            if(type is "image/jpeg" or "image/png"){
                try{stream=new(file.Content,false);picture=Image.FromStream(stream);}
                catch(Exception e)when(e is ArgumentException or OutOfMemoryException){throw new BusinessException("Không đọc được ảnh chứng từ. Vui lòng kiểm tra tệp gốc.");}
                viewer.Controls.Add(new PictureBox{Dock=DockStyle.Fill,Image=picture,SizeMode=PictureBoxSizeMode.Zoom});
            }else{
                viewer.Controls.Add(new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,
                    Text="Chứng từ "+(type=="application/pdf"?"PDF":"WEBP")+"\n\nChọn Lưu chứng từ để mở bằng ứng dụng phù hợp trên máy.",
                    Font=new Font("Segoe UI",12)});
            }
            viewer.Controls.Add(name);viewer.Controls.Add(footer);footer.BringToFront();name.BringToFront();
            save.Click+=async(_,_)=>await ErrorHandler.RunAsync(save,async()=>{
                var ext=Extension(type);
                using var dialog=new SaveFileDialog{Filter="Chứng từ (*"+ext+")|*"+ext,DefaultExt=ext.TrimStart('.'),
                    FileName="ChungTu"+ext,AddExtension=true,OverwritePrompt=true};
                if(dialog.ShowDialog(viewer)==DialogResult.OK)await File.WriteAllBytesAsync(dialog.FileName,file.Content);
            });
            viewer.CancelButton=close;viewer.ShowDialog(owner);
        }finally{picture?.Dispose();stream?.Dispose();}
    }
}
