using System.Drawing.Printing;

namespace Homestay.GUI.Common;

// Presentation-only input. Amounts, totals and amount-in-words must come from BLL.
/// <summary>Nội dung một chứng từ cần in: tiêu đề, các dòng thông tin, bảng, chữ ký.</summary>
public sealed record PrintContent(string Title, IReadOnlyList<string> Information,
    IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, string Footer);

/// <summary>Tạo và xem trước bản in chứng từ (phiếu thu, phiếu chi, bảng đối soát).</summary>
public static class DocumentPrinter
{
    public static PrintDocument Create(PrintContent content)
    {
        if (content.Headers.Count == 0 || content.Rows.Any(r => r.Length != content.Headers.Count))
            throw new InvalidDataException("Dữ liệu bản in không khớp cột.");
        var document = new PrintDocument { DocumentName = content.Title };
        document.DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);
        var row = 0;
        var page = 0;
        document.BeginPrint += (_, _) => { row = 0; page = 0; };
        document.PrintPage += (_, args) =>
        {
            var graphics = args.Graphics ?? throw new InvalidOperationException("Không tạo được trang in.");
            using var bodyFont = new Font("Segoe UI", 10);
            using var boldFont = new Font("Segoe UI", 10, FontStyle.Bold);
            using var titleFont = new Font("Segoe UI", 16, FontStyle.Bold);
            using var format = new StringFormat { FormatFlags = StringFormatFlags.LineLimit };
            var margin = args.MarginBounds;
            var width = margin.Width;
            var y = (float)margin.Top;
            if (width < 250 || margin.Height < 300)
                throw new InvalidDataException("Khổ giấy hoặc lề quá nhỏ để in chứng từ.");
            var titleHeight = graphics.MeasureString(content.Title, titleFont, width).Height;
            graphics.DrawString(content.Title, titleFont, Brushes.Black, new RectangleF(margin.Left,y,width,titleHeight));
            y += titleHeight + 12;
            foreach (var line in content.Information)
            {
                var height = graphics.MeasureString(line, bodyFont, width).Height;
                graphics.DrawString(line, bodyFont, Brushes.Black, new RectangleF(margin.Left,y,width,height));
                y += height + 3;
            }
            y += 10;
            var cellWidth = (float)width / content.Headers.Count;
            var headerHeight = content.Headers.Max(h => graphics.MeasureString(h, boldFont, (int)cellWidth - 10).Height) + 12;
            for (var c = 0; c < content.Headers.Count; c++)
            {
                var rect = new RectangleF(margin.Left + c * cellWidth,y,cellWidth,headerHeight);
                graphics.FillRectangle(Brushes.Gainsboro, rect);
                graphics.DrawRectangle(Pens.Black, rect.X,rect.Y,rect.Width,rect.Height);
                graphics.DrawString(content.Headers[c],boldFont,Brushes.Black,
                    new RectangleF(rect.X+5,rect.Y+5,rect.Width-10,rect.Height-10),format);
            }
            y += headerHeight;
            var rowStart = y;
            var bottom = margin.Bottom - 38;
            while(row < content.Rows.Count)
            {
                var cells = content.Rows[row];
                var height = cells.Max(text => graphics.MeasureString(text ?? "",bodyFont,(int)cellWidth-10).Height) + 12;
                if (height > bottom - rowStart)
                    throw new InvalidDataException("Một dòng chứng từ dài hơn trang in. Vui lòng dùng khổ giấy lớn hơn.");
                if(y + height > bottom) break;
                for(var c=0;c<cells.Length;c++)
                {
                    var rect = new RectangleF(margin.Left+c*cellWidth,y,cellWidth,height);
                    graphics.DrawRectangle(Pens.Black,rect.X,rect.Y,rect.Width,rect.Height);
                    graphics.DrawString(cells[c] ?? "",bodyFont,Brushes.Black,
                        new RectangleF(rect.X+5,rect.Y+5,rect.Width-10,rect.Height-10),format);
                }
                y += height; row++;
            }
            args.HasMorePages = row < content.Rows.Count;
            if(!args.HasMorePages && !string.IsNullOrEmpty(content.Footer))
            {
                var height = graphics.MeasureString(content.Footer,bodyFont,width).Height;
                if(height > bottom - rowStart)
                    throw new InvalidDataException("Phần cuối chứng từ dài hơn trang in.");
                if(y+height+10>bottom) args.HasMorePages = true;
                else graphics.DrawString(content.Footer,bodyFont,Brushes.Black,new RectangleF(margin.Left,y+10,width,height));
            }
            page++;
            graphics.DrawString("Trang " + page,bodyFont,Brushes.Black,margin.Left,margin.Bottom-20);
        };
        return document;
    }
    public static void Preview(IWin32Window owner, PrintContent content)
    {
        using var document = Create(content);
        using var dialog = new PrintPreviewDialog { Document = document, Width = 1100, Height = 760 };
        dialog.ShowDialog(owner);
    }
}
