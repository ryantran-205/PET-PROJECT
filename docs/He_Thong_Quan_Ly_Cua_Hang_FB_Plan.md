# Kế Hoạch Phát Triển Hệ Thống Quản Lý Cửa Hàng Ăn Uống (F&B Management System)

Dự án phát triển hệ thống quản lý chuỗi cửa hàng ăn uống chuyên nghiệp (mô hình tương tự The Coffee House, Highlands Coffee). Hệ thống tích hợp quản trị phân tán, gọi món tại quầy/bàn không cần đăng nhập, xử lý bất đồng bộ tải cao và sẵn sàng triển khai trên hạ tầng Microsoft Azure.

---

## 1. Kiến Trúc Hệ Thống & Lựa Chọn Công Nghệ

### 1.1. Công Nghệ Phát Triển
*   **Backend:** **.NET 10 Web API** (C# 14), tối ưu hóa bằng Native AOT cho các dịch vụ xử lý đơn hàng, sử dụng Minimal APIs để tăng hiệu năng xử lý.
*   **Frontend:** **Vue 3** (Composition API, Script Setup), Quản lý trạng thái bằng **Pinia**, Router bằng **Vue Router**, Bundler bằng **Vite**.
*   **UI Library:** **PrimeVue v5** cấu hình giao diện responsive (PC, Tablet, Mobile) qua hệ thống Design Tokens và Tailwind CSS (hoặc PrimeFlex/UnoCSS tùy biến phong cách Core-Unstyled).
*   **Real-time Communication:** **SignalR Core** quản lý điều phối số thứ tự đơn hàng, đồng bộ trạng thái đơn hàng thời gian thực giữa màn hình nhà bếp, nhân viên và khách hàng.
*   **Asynchronous Processing:** **Azure Service Bus** (hoặc **RabbitMQ** chạy môi trường Local) đóng vai trò Message Queue điều phối hàng đợi xử lý thanh toán, trừ kho vật liệu và xuất hóa đơn tránh nghẽn luồng.

### 1.2. Giải Pháp Cơ Sở Dữ Liệu (Database Choice)
Hệ thống áp dụng kiến trúc **Polyglot Persistence** để tối ưu hóa hiệu năng theo từng nghiệp vụ:
1.  **Cơ sở dữ liệu chính (Operational DB): SQL Server (Azure SQL Database)**
    *   *Lý do:* Đảm bảo tính toàn vẹn dữ liệu tuyệt đối (ACID) cho các nghiệp vụ quản lý nhân sự, phân ca, tính lương, quản lý kho bãi vật liệu (Xuất-Nhập-Tồn), dữ liệu tài chính hóa đơn lịch sử giao dịch.
    *   *Thiết kế phân tán:* Phân tách dữ liệu theo `BranchId` để hỗ trợ Supper Admin quản lý toàn chuỗi và Admin chi nhánh quản lý cục bộ một cách độc lập.
2.  **Cơ sở dữ liệu lưu trữ / Cache (Caching & Session): Redis (Azure Cache for Redis)**
    *   *Lý do:* Lưu trữ giỏ hàng tạm thời, danh mục thực đơn (Menu Foods/Drinks) tối ưu tốc độ đọc của khách hàng không cần truy vấn SQL liên tục, quản lý Real-time Queue ID khóa số thứ tự tự động tăng cho khách Order.

---

## 2. Phân Cấp Phân Quyền Hệ Thống (RBAC)

1.  **Super Admin (Quản trị hệ thống toàn chuỗi):** Toàn quyền cấu hình hệ thống, khởi tạo chi nhánh mới, chỉ định Admin chi nhánh, xem báo cáo doanh thu tổng hợp toàn bộ chuỗi cửa hàng.
2.  **Branch Admin (Quản lý chi nhánh):** Quản lý nhân viên thuộc chi nhánh, phân ca làm việc, duyệt lịch nghỉ phép, kiểm kê xuất nhập nguyên liệu đơn hàng tại kho chi nhánh, xuất báo cáo tài chính hóa đơn của chi nhánh mình phụ trách.
3.  **Branch Staff (Nhân viên chi nhánh):** Xem ca làm việc cá nhân, đăng nhập ca (Check-in/Check-out), tiếp nhận đơn hàng theo số thứ tự từ Message Queue, cập nhật trạng thái chế biến/giao đồ, in hóa đơn cho khách.
4.  **Customer (Người mua hàng - Không cần đăng nhập):** Truy cập menu qua mã QR tại bàn hoặc ki-ốt quầy, thực hiện order chọn món, hệ thống cấp phát **Số thứ tự (Order Number)** tự động, theo dõi tiến độ đơn hàng thời gian thực qua SignalR.

---

## 3. Kế Hoạch Phát Triển Chi Tiết & Chiến Lược Commit Code

Dự án được chia làm 5 giai đoạn rõ ràng. Mỗi giai đoạn tương ứng với các nhánh tính năng (`feature/*`) và tag phát hành để hỗ trợ Code Review hiệu quả.

```
[Môi trường Local: Docker] ---> [Nhánh develop] ---> [Nhánh main] ---> [CI/CD Pipeline] ---> [Môi trường Azure Cloud]
```

### Giai Đoạn 1: Thiết Lập Hạ Tầng & Khởi Tạo Dự Án (Tuần 1-2)
*   **Mục tiêu:** Tạo cấu trúc Solution Backend, Boilerplate Frontend, Setup DB migrations ban đầu, cấu hình Profiles độc lập.
*   **Chi tiết Backend:** Khởi tạo cấu trúc Clean Architecture (.NET 10), thiết lập DbContext, cài đặt Identity JWT Base. Cấu hình song song 2 Environment Profiles: `appsettings.Development.json` (Local DB, Docker RabbitMQ) và `appsettings.Production.json` (Azure SQL, Azure Service Bus).
*   **Chi tiết Frontend:** Cấu hình dự án Vue 3 với Vite, tích hợp PrimeVue v5, cấu hình Theme (Aura/Lara Preset), xây dựng Layout khung Responsive (Sidebar cho Admin, Topbar cho Khách).
*   **Chiến lược Commit Code:**
    *   `feat(shared): khởi tạo cấu trúc clean architecture backend với .net 10`
    *   `feat(infra): setup docker-compose chạy sql server, redis, rabbitmq local`
    *   `feat(frontend): setup vue3, pinia, primevue v5 và cấu hình responsive layout`

### Giai Đoạn 2: Tính Năng Quản Trị Nhân Sự & Ca Làm Việc (Tuần 3-4)
*   **Mục tiêu:** Hoàn thiện phân hệ Admin quản lý nhân sự, chấm công, ca kíp và tính toán lương gốc.
*   **Chi tiết Backend:** API CRUD Nhân viên, API cấu hình Ca làm việc (Giờ bắt đầu, kết thúc, hệ số lương). API Check-in/Check-out lưu trữ ngày giờ đăng nhập thực tế kết hợp IP/Vị trí cửa hàng. API Đăng ký và phê duyệt nghỉ phép. Nghiệp vụ tự động tổng hợp số ca, giờ làm để tính lương cuối tháng/tuần theo cấu hình.
*   **Chi tiết Frontend (PrimeVue v5):** Dùng `DataTable` hiển thị danh sách nhân viên phân trang; `Calendar` dạng Time-picker quản lý ca làm; `FullCalendar` hoặc `Timeline` trực quan hóa lịch làm việc.
*   **Chiến lược Commit Code:**
    *   `feat(auth): viết api login, ngày giờ đăng nhập chấm công và quản lý jwt`
    *   `feat(hr): api và giao diện quản lý nhân viên, lịch nghỉ phép chi nhánh`
    *   `feat(payroll): xây dựng logic tính lương theo ca và xuất file thống kê excel`

### Giai Đoạn 3: Quản Lý Kho Nguyên Liệu & Thực Đơn (Tuần 5-6)
*   **Mục tiêu:** Kiểm soát chuỗi cung ứng nội bộ, định lượng món ăn (Highlands/Coffee House style) và quản lý hóa đơn nguyên liệu đầu vào.
*   **Chi tiết Backend:** Quản lý danh mục nguyên liệu (Hạt cà phê, sữa, syrup, ly nhựa, ống hút...). Nghiệp vụ Xuất/Nhập kho chi nhánh, cảnh báo tồn kho tối thiểu. Thiết lập định lượng món ăn (Ví dụ: 1 ly Latte cần 20g hạt cà phê + 150ml sữa tươi), tự động trừ kho nguyên liệu khi hoàn tất đơn hàng.
*   **Chi tiết Frontend:** Giao diện Form phức hợp bằng PrimeVue `Dialog`, `InputNumber`, `Dropdown` chọn nguyên liệu. Hệ thống cảnh báo màu sắc tồn kho bằng `Badge` nguy hiểm.
*   **Chiến lược Commit Code:**
    *   `feat(inventory): thiết kế database và api quản lý xuất nhập tồn nguyên liệu`
    *   `feat(menu): api quản lý món ăn, combo đồ uống và định lượng kho tự động`

### Giai Đoạn 4: Phân Hệ Gọi Món Bất Đồng Bộ & SignalR Real-time (Tuần 7-8)
*   **Mục tiêu:** Lõi vận hành tải cao. Khách gọi món không cần đăng nhập, đẩy vào hàng đợi xử lý, cập nhật trạng thái bếp thời gian thực.
*   **Chi tiết Backend:** API Tiếp nhận Đơn hàng (`POST /api/orders`) đẩy dữ liệu thô vào Message Queue (RabbitMQ / Azure Service Bus) để xử lý bất đồng bộ nhằm phản hồi siêu tốc (<50ms) cho khách hàng. Worker Service tiêu thụ Queue xử lý tuần tự: Giảm kho định lượng, tạo hóa đơn, cấp phát Số thứ tự (`OrderNumber`). Tích hợp **SignalR Hub** phát tín hiệu Đơn hàng mới đến màn hình nhà bếp (`KitchenView`) và màn hình hiển thị số thứ tự khách hàng (`QueueDisplayView`).
*   **Chi tiết Frontend:** Trang gọi món cho Khách (Thiết kế Mobile-First dạng Grid danh mục đồ ăn/uống mượt mà). Màn hình Nhân viên nhận đơn tự động cập nhật không cần F5 nhờ SignalR Hub Connection.
*   **Chiến lược Commit Code:**
    *   `feat(queue): tích hợp message queue giải quyết bài toán đặt món đồng thời tải cao`
    *   `feat(signalr): thiết lập hub real-time đẩy số thứ tự xuống màn hình hiển thị`
    *   `feat(client-order): giao diện mobile-first cho khách hàng gọi món không đăng nhập`

### Giai Đoạn 5: Tài Chính, Xuất Hóa Đơn PDF/CSV & Đóng Gói Triển Khai (Tuần 9-10)
*   **Mục tiêu:** Kết xuất dữ liệu tài chính và đóng gói chuyển giao vận hành hạ tầng đám mây.
*   **Chi tiết Backend:** Viết service tích hợp thư viện xuất hóa đơn tự động sang định dạng **PDF** (Sử dụng ReportLab thông qua cổng chuyển đổi hoặc QuestPDF/iTextSharp chính thống trên .NET) gửi kèm định dạng **CSV** phục vụ kiểm toán kế toán chi nhánh. Viết kịch bản triển khai Github Actions CI/CD tự động lên Azure.
*   **Chi tiết Frontend:** Tích hợp Dashboard biểu đồ doanh thu, lịch sử giao dịch toàn chuỗi bằng `PrimeVue Charts` (Chart.js integration).
*   **Chiến lược Commit Code:**
    *   `feat(report): xây dựng công cụ render hóa đơn pdf và kết xuất dữ liệu csv`
    *   `feat(dashboard): màn hình thống kê lịch sử giao dịch và doanh thu tài chính`
    *   `docs: hoàn thiện tài liệu hướng dẫn vận hành, kịch bản deployment`

---

## 4. Đặc Tả File Hóa Đơn Xuất Bản

### 4.1. Định dạng Hóa đơn Khách hàng (PDF)
*   **Kích thước chuẩn:** Phù hợp in nhiệt khổ K80 (80mm) hoặc A5 phục vụ lưu trữ.
*   **Nội dung hiển thị:**
    *   Tên Thương hiệu & Địa chỉ chi nhánh cụ thể.
    *   **MÃ SỐ THỨ TỰ ĐƠN HÀNG (ORDER NUMBER):** In font lớn nổi bật ở đầu trang để khách nhận diện tại quầy.
    *   Bảng chi tiết mặt hàng: Tên món, Kích cỡ (S/M/L), Ghi chú (Ít đường/đá), Số lượng, Đơn giá, Thành tiền.
    *   Thông tin thanh toán: Tổng tiền hàng, Giảm giá (nếu có), Phương thức (Tiền mặt/Chuyển khoản QR), Thuế VAT.
    *   Chân trang: Lời cảm ơn và mã QR tra cứu hóa đơn điện tử trực tuyến.

### 4.2. Định dạng Báo cáo Doanh thu (CSV)
*   **Bảng mã:** UTF-8 có BOM (đảm bảo không lỗi font tiếng Việt khi mở trực tiếp bằng Microsoft Excel).
*   **Cấu trúc các cột chuẩn dữ liệu:**
    `InvoiceNo, BranchId, CreatedDate, AttendantStaffId, OrderNumber, TotalAmount, VatAmount, DiscountAmount, PaymentMethod, Status`
*   *Lưu ý thiết kế:* Tách biệt báo cáo xuất nguyên kho vật liệu độc lập với báo cáo hóa đơn thành phẩm đồ uống để kế toán dễ dàng đối chiếu số liệu hao hụt thực tế.
