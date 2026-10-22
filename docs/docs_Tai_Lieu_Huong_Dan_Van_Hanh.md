# Tài Liệu Vận Hành, Phát Triển & Triển Khai Hệ Thống F&B Management

Tài liệu này chứa các thông tin đặc tả môi trường cấu hình, hướng dẫn khởi chạy cục bộ, quy trình triển khai lên Azure Cloud Services, và thiết kế kịch bản kiểm thử Postman.

---

## 1. Lưu Ý Kỹ Thuật Khi Phát Triển & Vận Hành
1.  **Quản lý Số thứ tự khách hàng (Order Number):** Số thứ tự đặt món được thiết lập reset về giá trị `1` vào lúc 00:00:00 mỗi ngày. Sử dụng nguyên tử tính `INCR` của **Redis** kết hợp khóa phân tán để đảm bảo không bị trùng lặp số thứ tự khi có hàng ngàn khách hàng quét mã gọi món đồng thời cùng một giây.
2.  **Đồng bộ kho bằng Message Queue:** Khi khách đặt món thành công, hệ thống không trừ trực tiếp SQL Database để tránh khóa bảng (`Table Lock`). Đơn hàng được đẩy vào Hàng đợi, Worker sẽ xử lý tuần tự giúp hệ thống chịu tải tốt, không xảy ra hiện tượng lệch kho khi bán đồng thời.
3.  **Bảo mật phân quyền:** Kiểm tra quyền hạn chặt chẽ ở tầng API Middleware (.NET 10 Authorize Attribute) theo các Claims chứa `BranchId`. Nhân viên chi nhánh A tuyệt đối không thể đọc dữ liệu hóa đơn hoặc sửa đổi kho của chi nhánh B.

---

## 2. Tình Trạng Hoàn Thiện Sản Phẩm (Definition of Done)
Một tính năng được coi là hoàn thiện và sẵn sàng chuyển giao khi đáp ứng đủ:
*   [ ] **Backend:** Hoàn thành Code API, vượt qua Unit Test (>80% coverage), tích hợp đầy đủ kiểm soát log lỗi tập trung qua Serilog.
*   [ ] **Frontend:** Giao diện hiển thị chuẩn xác không vỡ khung trên cả 3 kích thước: iPhone 14 (Mobile), iPad Air (Tablet), và Macbook Pro 16 inch (PC).
*   [ ] **Hiệu năng:** API gọi món phản hồi dữ liệu thô (đã nhận hàng đợi) trong thời gian < 50ms ở điều kiện stress test 1000 requests/giây.

---

## 3. Quản Lý Profiles Chạy Local Riêng & Cloud Riêng

Hệ thống phân tách môi trường triệt để thông qua biến môi trường `ASPNETCORE_ENVIRONMENT`.

### 3.1. Cấu Hình Môi Trường Local (`Development`)
Sử dụng file cấu hình Backend `appsettings.Development.json` kết hợp tệp `docker-compose.yml`. Các kết nối hướng về Docker Container chạy nội bộ máy lập trình viên.
*   **Cơ sở dữ liệu:** `Server=localhost,1433;Database=FbManagementDb;User Id=sa;Password=LocalSecurePassword123!;TrustServerCertificate=True;`
*   **Message Queue (RabbitMQ):** `amqp://guest:[email protected]:5672/`
*   **Cache Service (Redis):** `localhost:6379,password=LocalRedisPass`

### 3.2. Cấu Hình Môi Trường Cloud (`Production` - Azure Deployment)
Cấu hình nạp trực tiếp qua **Azure App Service Configuration** để bảo mật, không lưu cứng mật khẩu vào mã nguồn (git repository).
*   **Cơ sở dữ liệu:** Kết nối đến **Azure SQL Database** (Sử dụng Managed Identity để xác thực an toàn không lộ Password chuỗi kết nối).
*   **Message Queue:** Kết nối đến **Azure Service Bus Connection String** (Dạng cổng Namespace chuyên dụng chịu tải cao của Microsoft Azure).
*   **Cache Service:** Kết nối đến **Azure Cache for Redis** qua cổng kết nối mã hóa SSL 6380.

---

## 4. Hướng Dẫn Chạy Ứng Dụng Tại Môi Trường Local (Local Run Guide)

### Bước 1: Khởi động các hạ tầng bổ trợ bằng Docker
Di chuyển vào thư mục gốc chứa file `docker-compose.yml` chạy lệnh sau để khởi chạy SQL Server, Redis, RabbitMQ cục bộ:
```bash
docker-compose up -d
```

### Bước 2: Khởi chạy và Migrations Backend (.NET 10)
Di chuyển vào thư mục dự án Backend (Thư mục chứa file `.csproj` của tầng Web API):
```bash
# Thực hiện cập nhật cấu trúc bảng vào Database Docker
dotnet ef database update --environment Development

# Khởi chạy ứng dụng với Profile Development
dotnet run --launch-profile "Development"
```
Ứng dụng API sẽ khởi chạy mặc định tại đường dẫn: `https://localhost:5001` (Kèm tài liệu Swagger/OpenAPI hiển thị tại `https://localhost:5001/swagger`).

### Bước 3: Khởi chạy mã nguồn Frontend (Vue 3 + PrimeVue v5)
Di chuyển vào thư mục source code Frontend:
```bash
# Cài đặt toàn bộ thư viện liên quan
npm install

# Khởi chạy dự án local development server
npm run dev
```
Giao diện Web Client sẽ chạy tại link: `http://localhost:5173`. Bạn có thể dùng DevTools kiểm thử responsive trực tiếp.

---

## 5. Quy Trình Triển Khai Lên Azure (Azure Deployment Guideline)

Quy trình triển khai tự động hóa qua hệ thống CI/CD (GitHub Actions / Azure DevOps):

1.  **Bước 1: Khởi tạo Tài nguyên trên Cổng Azure Portal**
    *   Tạo một **Resource Group** duy nhất để quản lý tập trung toàn hệ thống.
    *   Khởi tạo **Azure SQL Database** (Lựa chọn cấu hình Serverless tính phí theo lượng truy cập thực tế để tiết kiệm chi phí ban đầu).
    *   Khởi tạo dải hàng đợi **Azure Service Bus** (Tạo sẵn 1 Queue tên là `order-processing-queue`).
    *   Khởi tạo **Azure App Service** cho Backend API và cấu hình chạy Docker Container hoặc biên dịch mã nguồn trực tiếp .NET 10 isolated runtime.
    *   Khởi tạo **Azure Static Web Apps** để lưu trữ mã nguồn tĩnh Frontend Vue 3 nhằm tối ưu hóa chi phí truyền tải qua mạng lưới Content Delivery Network (CDN).
2.  **Bước 2: Cấu hình CI/CD Pipeline (GitHub Actions File Setup)**
    *   Kịch bản sẽ tự động kích hoạt khi có hành động `push` hoặc `pull_request` merged vào nhánh `main`.
    *   *Luồng xử lý Backend:* Build source code -> Chạy tự động các Unit Test -> Đóng gói Publish Artifact -> Deploy lên Azure App Service thông qua file mã khóa bảo mật `Publish Profile`.
    *   *Luồng xử lý Frontend:* Chạy `npm run build` tạo thư mục nén `/dist` -> Đẩy trực tiếp lên Azure Static Web Apps thông qua khóa định danh Deployment Token.

---

## 6. Đặc Tả Kịch Bản Postman Collection & Biến Môi Trường

Hệ thống cung cấp file cấu hình Postman đồng bộ giúp lập trình viên và Tester dễ dàng tái sử dụng bộ API để tự động hóa kiểm thử.

### 6.1. Cấu Hình Biến Môi Trường Trong Postman (Environment Variables)

Bạn cần tạo 2 Môi trường trong ứng dụng Postman: **FB_Local_Env** và **FB_Azure_Cloud_Env**. Các API sẽ sử dụng chung cú pháp biến đặt trong dấu ngoặc nhọn dạng `{{variable_name}}`.

| Tên biến (Variable Key) | Giá trị môi trường Local (`FB_Local_Env`) | Giá trị môi trường Cloud (`FB_Azure_Cloud_Env`) | Ghi chú |
| :--- | :--- | :--- | :--- |
| `baseUrl` | `https://localhost:5001` | `https://api-fb-management.azurewebsites.net` | Đường dẫn máy chủ API |
| `accessToken` | *Tự động cập nhật qua Script Login* | *Tự động cập nhật qua Script Login* | Token JWT xác thực quyền truy cập |
| `currentBranchId` | `1` | `12` | ID Chi nhánh đang thực hiện test |
| `testOrderNumber` | *Tự động lưu trữ khi tạo đơn* | *Tự động lưu trữ khi tạo đơn* | Số thứ tự đơn hàng dùng để đối chiếu |

### 6.2. Postman Pre-request & Test Scripts Tự Động Hóa
Để tăng tính tái sử dụng, tại API Đăng nhập (`POST /api/auth/login`), cấu hình đoạn mã **Tests Script** sau để trích xuất Token JWT từ phản hồi phản hồi của server và nạp thẳng vào biến môi trường hệ thống:

```javascript
// Postman Tests Script cho API Đăng nhập
if (pm.response.code === 200) {
    var jsonData = pm.response.json();
    // Lưu Token vào biến môi trường để các Request sau tự động sử dụng mà không cần copy tay
    pm.environment.set("accessToken", jsonData.token);
    pm.environment.set("currentBranchId", jsonData.user.branchId);
    console.log("Đã cập nhật Access Token thành công vào hệ thống Postman.");
}
```

Tại mục cấu hình **Authorization** của thư mục cha (Root Collection), chọn kiểu xác thực là **Bearer Token** và điền chuỗi giá trị cấu hình là `{{accessToken}}`. Mọi Request API viết thêm vào sau này sẽ tự động kế thừa cơ chế bảo mật này.
