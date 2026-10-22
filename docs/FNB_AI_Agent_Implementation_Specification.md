# AI Agent Implementation Specification
## F&B Management System — Multi-Branch Restaurant/Coffee Shop Platform

> **Mục đích tài liệu:** Đây là đặc tả triển khai để AI coding agent có thể tự khảo sát repository, thiết kế, lập trình, kiểm thử, cập nhật tài liệu và chuẩn bị triển khai hệ thống quản lý chuỗi cửa hàng F&B.
>
> **Nguyên tắc:** Không chỉ tạo code mẫu hoặc giao diện giả. Mỗi tính năng được coi là hoàn thành khi có implementation thực tế, validation, phân quyền, persistence, xử lý lỗi, kiểm thử và tài liệu cần thiết.

---

## 1. Vai trò và cách làm việc của AI agent

Bạn là Senior Software Architect kiêm Senior Full-stack Engineer. Hãy chủ động triển khai dự án theo từng lát chức năng hoàn chỉnh (vertical slices), ưu tiên tính đúng đắn nghiệp vụ, bảo mật, khả năng bảo trì và khả năng vận hành.

### 1.1. Quy trình bắt buộc

1. Khảo sát repository hiện tại trước khi sửa: cấu trúc thư mục, solution/project, package versions, conventions, migrations, tests, CI/CD và các tính năng đã có.
2. Nếu repository đã có code, **bảo toàn và mở rộng** kiến trúc hiện có nếu phù hợp; không tạo lại dự án hoặc ghi đè file hàng loạt khi chưa cần thiết.
3. Tạo `docs/implementation-plan.md` gồm các phase, task, dependency, rủi ro và tiêu chí hoàn thành. Tạo `docs/assumptions-and-decisions.md` ghi các giả định và quyết định kiến trúc.
4. Triển khai theo thứ tự trong tài liệu. Với mỗi task:
   - Cập nhật code và cấu hình liên quan.
   - Tạo/cập nhật database migration nếu schema thay đổi.
   - Viết unit/integration tests.
   - Chạy formatter, lint, tests và build phù hợp.
   - Sửa lỗi do thay đổi gây ra trước khi chuyển task tiếp theo.
   - Cập nhật README/API docs nếu cần.
5. Không tuyên bố build/test/deployment thành công nếu chưa thực sự chạy được. Nếu môi trường không hỗ trợ chạy một bước, ghi rõ bước đó, lý do và lệnh cần chạy.
6. Không để `TODO`, mock data, hard-coded success hoặc endpoint giả trong luồng nghiệp vụ được tuyên bố là hoàn thành. Placeholder chỉ được phép khi được ghi rõ trong tài liệu và không làm giả kết quả nghiệp vụ.
7. Nếu thiếu quyết định không thể suy ra an toàn (ví dụ nhà cung cấp thanh toán, quy định VAT, chính sách lưu dữ liệu cá nhân), chọn giải pháp không phá hủy dữ liệu, ghi giả định và tạo adapter/interface để thay thế sau này. Chỉ hỏi người dùng khi quyết định đó chặn việc triển khai hoặc có rủi ro pháp lý/tài chính/bảo mật đáng kể.
8. Không tự ý thêm công nghệ lớn ngoài stack đã chốt. Nếu phát hiện xung đột kỹ thuật, trình bày ngắn gọn vấn đề, chọn phương án ít thay đổi nhất và ghi lại quyết định.
9. Mỗi giai đoạn phải để repository ở trạng thái nhất quán; không để code không compile ở cuối một chặng.
10. Trước khi kết thúc, cung cấp summary: tính năng đã làm, file/thành phần chính, migration, lệnh build/test đã chạy và kết quả, giả định còn tồn tại, việc chưa hoàn thành.

### 1.2. Định nghĩa hoàn thành (Definition of Done)

Một tính năng chỉ được xem là hoàn thành khi:
- Có luồng nghiệp vụ thực tế từ UI/API đến database hoặc message handler tương ứng.
- Validation phía server là bắt buộc; validation phía client chỉ hỗ trợ trải nghiệm.
- Có xác thực/phân quyền phù hợp, bao gồm kiểm tra phạm vi chi nhánh.
- Có xử lý lỗi, logging có cấu trúc và không làm lộ secrets/dữ liệu nhạy cảm.
- Có unit tests cho logic quan trọng và integration tests cho luồng persistence/integration trọng yếu.
- Build và tests liên quan chạy thành công hoặc các giới hạn môi trường được ghi rõ.
- Có migration, cấu hình môi trường và tài liệu cập nhật nếu có thay đổi tương ứng.
- UI có loading, empty, success, validation-error và general-error states.
- Không có secret, connection string nhạy cảm hoặc dữ liệu khách hàng thật được commit vào source control.

---

## 2. Mục tiêu sản phẩm

Xây dựng hệ thống quản lý chuỗi cửa hàng F&B (cà phê, đồ uống, đồ ăn nhẹ) có nhiều chi nhánh. Hệ thống hỗ trợ quản trị toàn chuỗi, vận hành từng chi nhánh, nhân sự và ca làm, kho nguyên liệu, menu, tiếp nhận đơn hàng, điều phối bếp, theo dõi trạng thái đơn hàng thời gian thực, hóa đơn và báo cáo.

Khách hàng có thể mở menu qua QR tại bàn hoặc kiosk/quầy và đặt món **không cần tạo tài khoản/đăng nhập**. Nhân viên và quản trị viên phải đăng nhập theo quyền được cấp.

### 2.1. Mục tiêu chất lượng

- Multi-branch: dữ liệu được phân vùng theo `BranchId`; Super Admin có quyền toàn chuỗi, người dùng chi nhánh chỉ truy cập chi nhánh được cấp.
- Xử lý đơn hàng bất đồng bộ, có khả năng retry và chống xử lý trùng.
- Đồng bộ trạng thái bếp/quầy/màn hình khách qua SignalR.
- Tính toàn vẹn dữ liệu cho đơn hàng, hóa đơn, tồn kho và giao dịch.
- Giao diện responsive cho desktop, tablet và mobile.
- Chạy local bằng Docker Compose; triển khai mục tiêu lên Microsoft Azure.
- Có logging, health checks, metrics cơ bản, CI/CD và hướng dẫn vận hành.
- Không cam kết latency cụ thể nếu chưa đo benchmark. Mục tiêu tiếp nhận API nhanh là một yêu cầu cần đo bằng load test, không được đánh đổi tính đúng đắn để đạt con số giả định.

### 2.2. Ngoài phạm vi mặc định

Trừ khi được yêu cầu bổ sung, không tự triển khai:
- Tích hợp cổng thanh toán thật hoặc lưu thông tin thẻ.
- Hóa đơn điện tử có giá trị pháp lý/tích hợp nhà cung cấp hóa đơn điện tử.
- Đồng bộ với hệ thống kế toán/ERP bên ngoài.
- Ứng dụng mobile native.
- Tính lương theo luật/lao động phức tạp, thuế thu nhập cá nhân hoặc bảo hiểm.
- Định vị GPS nhân viên. Chỉ ghi IP và chi nhánh/thiết bị theo chính sách được cấu hình; không giả lập GPS.
- Loyalty, voucher nâng cao, giao hàng, đặt bàn, chương trình thành viên.
- Multi-currency. Mặc định sử dụng VND.

---

## 3. Công nghệ và quy tắc kiến trúc

### 3.1. Backend

- .NET 10, C# 14, ASP.NET Core Web API.
- Ưu tiên Minimal APIs hoặc endpoint conventions nhất quán; không trộn nhiều phong cách không có lý do.
- Clean Architecture theo hướng đơn giản, không tạo abstraction không cần thiết.
- Gợi ý solution:
  - `src/Fnb.Api` — HTTP endpoints, middleware, authentication, OpenAPI.
  - `src/Fnb.Application` — use cases, DTOs, validation, interfaces.
  - `src/Fnb.Domain` — entities, value objects, domain rules.
  - `src/Fnb.Infrastructure` — EF Core, SQL Server, Redis, Service Bus, file export.
  - `src/Fnb.Worker` — background consumers và scheduled jobs.
  - `src/Fnb.Contracts` — message contracts dùng chung nếu cần.
  - `tests/Fnb.UnitTests`
  - `tests/Fnb.IntegrationTests`
- EF Core với SQL Server/Azure SQL; migration phải được quản lý bằng source control.
- JWT bearer cho API của nhân viên/quản trị viên. Thiết kế access token ngắn hạn và refresh-token flow an toàn nếu triển khai refresh tokens.
- Không áp dụng Native AOT cho toàn bộ hệ thống theo mặc định. Chỉ bật cho service phù hợp sau khi kiểm tra compatibility của EF Core, serializers, reflection và dependencies; build AOT là tiêu chí riêng nếu được chọn.
- Dùng `ProblemDetails` hoặc response error format thống nhất; không trả stack trace cho client ở production.
- API có versioning convention rõ ràng, mặc định prefix `/api/v1`.

### 3.2. Frontend

- Vue 3, TypeScript, Composition API, `<script setup>`.
- Vite, Vue Router, Pinia.
- PrimeVue v5; chọn một hệ thống styling chính. Mặc định PrimeVue theme/tokens kết hợp CSS responsive; chỉ thêm Tailwind CSS nếu repository đã dùng hoặc có lợi ích rõ ràng. Không đồng thời thêm PrimeFlex, UnoCSS và Tailwind một cách tùy tiện.
- Gợi ý cấu trúc:
  - `src/app` — app bootstrap, router, providers.
  - `src/layouts` — admin, staff, customer layouts.
  - `src/modules/<feature>` — pages, components, stores, services, types.
  - `src/shared` — reusable components, API client, utilities.
- API client phải tập trung xử lý token, timeout, correlation ID, lỗi HTTP và serialization.
- Không tin cậy quyền từ frontend; backend là nguồn quyết định cuối cùng.
- Có route guards cho trải nghiệm người dùng, nhưng API vẫn phải tự kiểm tra quyền.
- Có responsive layout cho desktop, tablet và mobile; ưu tiên mobile-first với màn hình khách gọi món.

### 3.3. Database và cache

- Operational database: SQL Server local; Azure SQL Database trên cloud.
- Redis local/cloud cho cache và dữ liệu tạm phù hợp.
- Dùng UTC để lưu thời điểm; hiển thị theo timezone cấu hình của chi nhánh. Mặc định `Asia/Ho_Chi_Minh`.
- Tiền tệ dùng `decimal` với precision được khai báo rõ ràng; không dùng `float`/`double` cho tiền.
- Dùng `decimal` cho số lượng nguyên liệu nếu cần phần lẻ; quy định đơn vị đo và precision.
- Các thao tác liên quan nhiều bảng cần transaction phù hợp.
- Không dùng Redis làm nguồn dữ liệu duy nhất cho đơn hàng, hóa đơn, số tiền hoặc tồn kho chính thức.
- Cache có TTL, chiến lược invalidation và fallback khi Redis không khả dụng.
- Có index cho các truy vấn thường dùng: `BranchId`, trạng thái, thời gian tạo, mã đơn/hóa đơn, khóa ngoại và các trường tìm kiếm phù hợp.

### 3.4. Message queue và event processing

- Production: Azure Service Bus.
- Local: RabbitMQ qua abstraction/interface để worker không phụ thuộc trực tiếp vào một broker cụ thể.
- Định nghĩa message contract có `MessageId`, `CorrelationId`, `OccurredAtUtc`, `SchemaVersion` và business identifier cần thiết.
- Bảo đảm **at-least-once delivery** và idempotent consumer; không tuyên bố exactly-once end-to-end.
- Dùng retry có giới hạn, exponential backoff và dead-letter queue. Có hướng dẫn kiểm tra/replay message lỗi.
- Không gửi toàn bộ entity hoặc dữ liệu nhạy cảm không cần thiết qua queue.
- Với thay đổi database cần phát event, cân nhắc Transactional Outbox để tránh tình trạng commit DB thành công nhưng publish message thất bại.
- Không thực hiện giao dịch SQL và thao tác broker như thể chúng thuộc cùng một transaction phân tán.

### 3.5. Real-time

- ASP.NET Core SignalR.
- Client chỉ được join group phù hợp sau khi backend xác minh quyền hoặc mã truy cập công khai hợp lệ.
- Tên group phải có namespace và branch scope, ví dụ `branch:{branchId}:kitchen`.
- Không phát dữ liệu của chi nhánh này sang chi nhánh khác.
- SignalR là kênh cập nhật nhanh, không phải nguồn dữ liệu chính thức. Khi reconnect, client phải lấy lại snapshot trạng thái từ API.
- Cấu hình Azure SignalR Service khi cần scale-out nhiều instance; nếu chưa cấu hình thì ghi rõ giới hạn của triển khai hiện tại.

---

## 4. Mô hình tenant, chi nhánh và phân quyền

### 4.1. Phạm vi dữ liệu

Hệ thống quản lý nhiều chi nhánh thuộc một tổ chức/chuỗi. Thiết kế cần có `OrganizationId` nếu cần hỗ trợ nhiều công ty/chuỗi trong tương lai; mặc định triển khai một tổ chức có nhiều `BranchId`, nhưng không hard-code mọi logic vào một chi nhánh duy nhất.

- `SuperAdmin`: truy cập toàn chuỗi, cấu hình hệ thống, chi nhánh, tài khoản quản trị và báo cáo tổng hợp.
- `BranchAdmin`: chỉ quản lý chi nhánh được gán; quản lý nhân viên, ca làm, nghỉ phép, menu khả dụng, kho, đơn hàng và báo cáo chi nhánh.
- `BranchStaff`: quyền theo chức năng được cấp tại chi nhánh; xem lịch cá nhân, check-in/out, tiếp nhận và cập nhật đơn hàng, in/xuất hóa đơn theo chính sách.
- `Customer`: không có tài khoản nội bộ; truy cập menu và theo dõi đơn bằng mã/secret công khai có giới hạn quyền.

### 4.2. Yêu cầu bảo mật phân quyền

- Áp dụng deny-by-default.
- Kiểm tra role và branch scope trên mọi endpoint có dữ liệu theo chi nhánh.
- Không tin `BranchId` do client gửi. Với user nội bộ, xác định chi nhánh từ claims/assignment hoặc xác thực rằng user được phép thao tác chi nhánh đó.
- Với Super Admin, thao tác xuyên chi nhánh phải rõ ràng và có audit log.
- Customer public token phải khó đoán, có entropy cao, chỉ cho phép đọc trạng thái đơn tương ứng; không dùng số thứ tự đơn hàng làm secret.
- Không trả thông tin nhân viên, dữ liệu nội bộ, giá vốn hoặc thông tin kho cho API công khai.
- Audit log cho đăng nhập, phân quyền, thay đổi menu/giá, điều chỉnh kho, hủy đơn, thay đổi thanh toán, xuất báo cáo và thao tác quản trị quan trọng.

---

## 5. Phân hệ chức năng

### 5.1. Authentication và quản trị người dùng

**Yêu cầu**
- Đăng nhập bằng thông tin xác thực của nhân viên/quản trị viên.
- Đăng xuất; xử lý token hết hạn.
- Quản trị tài khoản, trạng thái hoạt động, role và chi nhánh được gán.
- Có cơ chế đổi/reset mật khẩu an toàn nếu triển khai quản trị tài khoản nội bộ.
- Throttle/rate-limit login; không tiết lộ tài khoản có tồn tại hay không trong thông báo lỗi.
- Không lưu password dạng plain text; dùng ASP.NET Core Identity hoặc password hashing chuẩn đã được kiểm chứng.
- Secrets lấy từ environment/Key Vault, không commit vào repository.

**Tiêu chí nghiệm thu**
- User không hoạt động không thể đăng nhập.
- User chi nhánh A không đọc hoặc sửa tài nguyên chi nhánh B bằng cách thay ID trên URL/body.
- API trả 401 khi chưa xác thực và 403 khi đã xác thực nhưng không đủ quyền.

### 5.2. Quản lý chi nhánh

- CRUD chi nhánh: mã duy nhất, tên, địa chỉ, số điện thoại tùy chọn, timezone, trạng thái hoạt động.
- Không xóa cứng chi nhánh đã phát sinh đơn hàng hoặc giao dịch; dùng trạng thái inactive/soft delete phù hợp.
- Có cấu hình thông tin thương hiệu/chi nhánh dùng trên hóa đơn.
- Chỉ Super Admin tạo hoặc thay đổi cấu hình toàn chuỗi; Branch Admin chỉ được cập nhật trường được phép tại chi nhánh của mình.

### 5.3. Nhân sự, lịch làm và chấm công

**Dữ liệu và nghiệp vụ**
- Hồ sơ nhân viên: mã nhân viên duy nhất trong phạm vi quy định, tên, thông tin liên hệ cần thiết, trạng thái, role/assignment chi nhánh.
- Ca làm: tên ca, giờ bắt đầu/kết thúc, hệ số lương, chi nhánh, ngày áp dụng và trạng thái.
- Lịch phân ca theo nhân viên/ngày/ca; phát hiện xung đột lịch theo quy tắc đã định.
- Đăng ký nghỉ phép, duyệt/từ chối, lưu người duyệt và thời điểm xử lý.
- Check-in/check-out lưu thời điểm UTC thực tế, nhân viên, chi nhánh, nguồn thao tác và audit metadata cần thiết.
- Chặn check-in trùng hoặc check-out khi chưa check-in; xử lý ca qua nửa đêm.
- Tính tổng giờ làm theo quy tắc làm tròn được cấu hình và lưu kết quả có thể truy vết.
- Báo cáo giờ làm/ca làm theo tuần hoặc tháng.

**Giới hạn tính lương**
- Giai đoạn đầu tính lương cơ bản theo tổng giờ/ca và hệ số được cấu hình.
- Công thức phải được viết rõ, kiểm thử và lưu snapshot đầu vào/kết quả khi chốt kỳ.
- Không tuyên bố tương thích pháp luật tiền lương/thuế nếu chưa có yêu cầu và thẩm định chuyên biệt.
- Không tự sửa dữ liệu chấm công đã chốt mà không có quyền và audit trail.

### 5.4. Menu, món ăn/đồ uống và giá bán

- Quản lý danh mục, món, mô tả, ảnh URL hoặc asset, SKU/mã món, trạng thái bán.
- Món có kích cỡ/variant (ví dụ S/M/L), giá theo variant và chi nhánh hoặc bảng giá được cấu hình.
- Quản lý tùy chọn/add-on và ghi chú món như ít đường, ít đá, thêm shot nếu được cấu hình.
- Có thể bật/tắt bán tại từng chi nhánh.
- Giá tại thời điểm đặt hàng phải được snapshot vào order item; thay đổi giá sau đó không được sửa lịch sử đơn.
- Quản lý công thức/định lượng nguyên liệu cho từng món/variant.
- Public menu chỉ trả món đang được bán tại chi nhánh, giá công khai và thông tin cần thiết.
- Cache menu với TTL/invalidation khi có thay đổi; cache lỗi không được làm lộ dữ liệu hoặc trả giá sai lâu dài.

### 5.5. Quản lý kho nguyên liệu

**Dữ liệu**
- Nguyên liệu/vật tư: mã, tên, đơn vị đo cơ sở, trạng thái, ngưỡng tồn tối thiểu.
- Kho theo chi nhánh.
- Công thức định lượng theo món/variant, ví dụ 20 g cà phê + 150 ml sữa cho một ly.
- Phiếu nhập, xuất, điều chỉnh, kiểm kê; lý do, người thực hiện, thời gian và các dòng chi tiết.
- Sổ giao dịch kho (inventory ledger) là lịch sử bất biến theo nghiệp vụ; không chỉ lưu mỗi con số tồn hiện tại.
- Tồn hiện tại có thể là số dư được cập nhật có kiểm soát, đối soát được từ ledger.

**Quy tắc**
- Không cho tồn kho âm theo mặc định. Nếu nghiệp vụ cần cho phép, phải là cấu hình rõ ràng và có audit.
- Mọi điều chỉnh phải có lý do.
- Dùng transaction và concurrency control để tránh hai thao tác đồng thời cùng tiêu thụ một lượng tồn.
- Khi thay đổi công thức, không áp dụng hồi tố lên các đơn đã hoàn tất.
- Định nghĩa rõ thời điểm trừ kho. Mặc định: trừ kho khi đơn chuyển sang trạng thái được xác nhận/đưa vào chế biến; thao tác phải idempotent. Khi hủy sau thời điểm trừ kho, hoàn kho bằng một ledger transaction đối ứng, không xóa giao dịch cũ.
- Nếu không đủ tồn, xử lý theo chính sách đã định: từ chối xác nhận món/đơn hoặc yêu cầu nhân viên xử lý; không âm thầm ghi tồn âm.
- Cảnh báo khi tồn dưới ngưỡng tối thiểu.

### 5.6. Đặt món công khai không cần đăng nhập

**Luồng mặc định**
1. Khách quét QR của chi nhánh/bàn hoặc mở URL kiosk.
2. Backend xác thực mã chi nhánh/bàn công khai hợp lệ; không tin `BranchId` tùy ý từ client.
3. Khách xem menu, chọn variant, số lượng, add-on và ghi chú.
4. Client gửi yêu cầu tạo đơn với `Idempotency-Key` duy nhất.
5. API validate món, giá, trạng thái bán, số lượng và chi nhánh; tính lại toàn bộ giá phía server.
6. API lưu đơn ở trạng thái `PendingConfirmation`/`Submitted` theo mô hình đã thống nhất, đồng thời ghi outbox event trong cùng transaction.
7. API trả `OrderId`, `OrderNumber` nếu đã cấp, trạng thái và public tracking token. Không trả dữ liệu nội bộ.
8. Worker publish event, xử lý nghiệp vụ bất đồng bộ và cập nhật trạng thái.
9. SignalR gửi thông báo trạng thái; client vẫn có thể polling API snapshot nếu mất kết nối.

**Yêu cầu chống lạm dụng**
- Rate limit theo IP/chi nhánh và các tín hiệu phù hợp; không chỉ dựa vào IP.
- Validate số lượng tối đa, độ dài ghi chú, kích thước payload và các ID.
- CORS chỉ cho phép origin được cấu hình; không dùng wildcard kèm credentials.
- Không yêu cầu khách tạo tài khoản.
- Public tracking token phải ngẫu nhiên, không tuần tự, không suy ra được từ OrderId/OrderNumber.
- Không lưu dữ liệu cá nhân không cần thiết.

### 5.7. Xử lý đơn hàng, số thứ tự và hàng đợi bếp

**Trạng thái đề xuất**
- `Submitted`: yêu cầu đã được tiếp nhận.
- `PendingConfirmation`: đang chờ kiểm tra/xác nhận nghiệp vụ nếu tách bước.
- `Confirmed`: đơn được chấp nhận.
- `Preparing`: đang chế biến.
- `Ready`: sẵn sàng giao.
- `Completed`: đã giao/hoàn tất.
- `Cancelled`: đã hủy.
- `Rejected`: bị từ chối trước khi xác nhận.

Agent phải tạo state transition rõ ràng; không cho phép chuyển trạng thái tùy ý. Ví dụ `Completed` không thể quay về `Preparing` bằng API thông thường. Hủy đơn sau khi đã xác nhận phải tuân theo quyền và quy tắc hoàn kho/hoàn tiền; không tự giả định có hoàn tiền thật khi chưa tích hợp payment provider.

**Order number**
- Số thứ tự hiển thị cần duy nhất trong phạm vi chi nhánh và ngày kinh doanh (hoặc phạm vi khác nếu được quyết định).
- Không dùng `MAX(OrderNumber)+1` vì có race condition.
- Cấp số bằng cơ chế atomic/concurrency-safe ở SQL Server; tạo unique constraint cho phạm vi được chọn.
- Xác định timezone và business date của chi nhánh.
- Quyết định rõ có cấp số khi tiếp nhận hay khi xác nhận. Mặc định cấp số trong transaction tạo/tiếp nhận đơn để khách có mã theo dõi ngay; số có thể có khoảng trống nếu đơn thất bại/hủy và không được tái sử dụng.
- Idempotency key bảo đảm retry cùng request không tạo đơn mới.

**Worker**
- Consumer phải idempotent theo `MessageId`/business event ID.
- Có retry giới hạn, dead-letter và log `CorrelationId`.
- Không tạo hóa đơn hoặc trừ kho nhiều lần khi message được giao lại.
- Với lỗi tạm thời, retry; với lỗi nghiệp vụ không thể khắc phục, chuyển dead-letter hoặc trạng thái cần xử lý thủ công theo quy tắc.
- Xử lý tình huống DB commit thành công nhưng publish event thất bại bằng outbox.
- Xử lý cạnh tranh khi hai nhân viên cùng cập nhật trạng thái đơn.

### 5.8. Thanh toán và hóa đơn

**Giai đoạn đầu**
- Hỗ trợ ghi nhận phương thức thanh toán: tiền mặt hoặc chuyển khoản QR theo cấu hình.
- Nếu chưa tích hợp provider, payment record chỉ phản ánh thao tác ghi nhận của nhân viên, không được tuyên bố xác minh tự động giao dịch ngân hàng.
- Trạng thái thanh toán gợi ý: `Unpaid`, `Pending`, `Paid`, `Failed`, `Refunded`, `PartiallyRefunded` nếu nghiệp vụ cần. Chỉ cho phép transition hợp lệ.
- Không lưu thông tin thẻ hoặc bí mật thanh toán.
- Hóa đơn nội bộ được lưu cùng snapshot tên món, giá, số lượng, giảm giá, thuế, tổng tiền và phương thức thanh toán.
- Không gọi hóa đơn PDF nội bộ là hóa đơn điện tử có giá trị pháp lý.

**Công thức**
- Tổng dòng = đơn giá snapshot × số lượng + add-on hợp lệ.
- Tổng tiền hàng = tổng các dòng.
- Giảm giá, thuế và làm tròn phải được áp dụng theo một thứ tự được cấu hình, có test; không hard-code thuế suất nếu chưa được xác nhận.
- Dùng decimal; server tính lại tổng, không tin tổng tiền từ frontend.
- Lưu snapshot giá/thuế/giảm giá và quy tắc áp dụng để báo cáo lịch sử nhất quán.

### 5.9. SignalR và màn hình vận hành

- `KitchenView`: nhận đơn mới và trạng thái đơn thuộc chi nhánh được phép.
- `StaffView`: cập nhật trạng thái chế biến/giao hàng theo quyền.
- `QueueDisplayView`: hiển thị số đơn và trạng thái công khai tối thiểu, không hiển thị dữ liệu cá nhân.
- `CustomerOrderTracking`: theo dõi một đơn qua public tracking token.
- Có xử lý reconnect, trạng thái kết nối, snapshot reload và chống cập nhật UI trùng.
- Client không được tự khai báo group tùy ý để đọc dữ liệu của chi nhánh khác.

### 5.10. Báo cáo và xuất file

**PDF hóa đơn**
- Hỗ trợ layout phù hợp giấy nhiệt K80 và A5.
- Nội dung: thương hiệu/tên chi nhánh/địa chỉ; số thứ tự nổi bật; mã hóa đơn; thời gian; dòng hàng (tên, size, ghi chú, số lượng, đơn giá, thành tiền); tổng tiền, giảm giá, thuế, phương thức và trạng thái thanh toán; lời cảm ơn.
- QR tra cứu chỉ được tạo nếu có URL tra cứu hợp lệ và endpoint được bảo vệ đúng mức.
- Đảm bảo font tiếng Việt, encoding, ngắt dòng và in thử bằng test/render sample.
- Chọn thư viện .NET tương thích license và framework; ghi rõ license trong tài liệu. Không dùng ReportLab trực tiếp trong .NET backend trừ khi chủ động xây dựng một service Python riêng (mặc định không làm vậy).

**CSV doanh thu**
- UTF-8 BOM để tương thích Excel.
- Cột chuẩn tối thiểu:
  `InvoiceNo, BranchId, CreatedDate, AttendantStaffId, OrderNumber, TotalAmount, VatAmount, DiscountAmount, PaymentMethod, Status`
- Quy định định dạng ngày ISO 8601, decimal invariant và escaping dấu phẩy/ngoặc kép/xuống dòng.
- Chống CSV formula injection: escape các giá trị văn bản bắt đầu bằng `=`, `+`, `-`, `@` hoặc các ký tự nguy hiểm theo chính sách export.
- Kiểm tra quyền và branch scope trước khi export; giới hạn khoảng ngày/số dòng và dùng streaming/background export nếu dữ liệu lớn.
- Báo cáo kho phải độc lập với báo cáo hóa đơn bán hàng, để đối chiếu nhập-xuất-tồn và hao hụt.
- Tổng doanh thu phải định nghĩa rõ: theo thời điểm tạo đơn, hoàn tất, thanh toán hay ghi nhận hóa đơn. Mặc định báo cáo doanh thu đã thanh toán dựa trên invoice/payment status; các trạng thái khác được thể hiện riêng.

### 5.11. Dashboard

- Tổng doanh thu theo khoảng thời gian và chi nhánh.
- Số đơn theo trạng thái.
- Giá trị đơn trung bình nếu có dữ liệu đủ.
- Top món bán chạy.
- Cảnh báo nguyên liệu dưới ngưỡng.
- Tổng giờ/ca làm theo kỳ nếu user có quyền HR.
- Bộ lọc ngày, chi nhánh và trạng thái phù hợp role.
- Các chỉ số phải có định nghĩa trong tài liệu; dashboard không được tính tổng bằng cách cộng các bản ghi trùng do join sai.

---

## 6. Mô hình dữ liệu tối thiểu

Agent phải tạo ERD (Mermaid hoặc định dạng tương đương) tại `docs/data-model.md`, giải thích quan hệ, index, unique constraints, soft-delete và chính sách retention.

Các bảng/entity dự kiến (có thể điều chỉnh theo mô hình domain, nhưng phải ghi lý do):

- `Organizations` (nếu bật phạm vi nhiều tổ chức)
- `Branches`
- `Users`/ASP.NET Core Identity tables
- `Roles`, `UserBranchAssignments` hoặc cấu trúc claim/assignment tương đương
- `Employees`
- `WorkShifts`
- `EmployeeShiftAssignments`
- `LeaveRequests`
- `AttendanceRecords`
- `MenuCategories`
- `MenuItems`
- `MenuItemVariants`
- `AddOns` và bảng quan hệ nếu cần
- `BranchMenuAvailability`/`BranchPrices` nếu giá và tình trạng bán khác nhau theo chi nhánh
- `Ingredients`
- `BranchInventories`
- `Recipes`, `RecipeLines`
- `InventoryDocuments`, `InventoryDocumentLines`
- `InventoryLedgerEntries`
- `Orders`
- `OrderItems`
- `OrderItemAddOns`
- `OrderStatusHistory`
- `Invoices`, `InvoiceLines`
- `Payments`/`PaymentTransactions`
- `IdempotencyRecords`
- `OutboxMessages`
- `ProcessedMessages`/inbox deduplication
- `AuditLogs`

### 6.1. Quy tắc dữ liệu

- Khóa chính dùng GUID hoặc kiểu phù hợp nhất quán; không trộn kiểu không cần thiết.
- Entity theo chi nhánh phải có `BranchId` và index phù hợp.
- Đơn hàng và hóa đơn lưu snapshot tên/variant/đơn giá/thuế/giảm giá tại thời điểm giao dịch.
- Có unique constraint cho mã chi nhánh, mã nhân viên theo phạm vi, invoice number và order number theo phạm vi được chọn.
- Có concurrency token/rowversion cho dữ liệu dễ bị sửa đồng thời.
- Dùng decimal precision rõ ràng, ví dụ `decimal(18,2)` cho tiền và precision riêng cho lượng nguyên liệu.
- Xóa logic không được phá lịch sử giao dịch. Đơn, hóa đơn, ledger và audit log không xóa cứng qua CRUD thông thường.
- Tất cả migration phải chạy được trên database mới và được kiểm tra trong CI.

---

## 7. Hợp đồng API tối thiểu

Tất cả API quản trị dùng `/api/v1`; response, pagination, sorting và lỗi phải thống nhất. Có OpenAPI/Swagger trong môi trường phù hợp.

### 7.1. Authentication và branches
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh` nếu refresh token được triển khai
- `POST /api/v1/auth/logout`
- `GET /api/v1/me`
- `GET /api/v1/branches`
- `POST /api/v1/branches`
- `GET /api/v1/branches/{branchId}`
- `PUT /api/v1/branches/{branchId}`

### 7.2. HR
- `GET/POST /api/v1/branches/{branchId}/employees`
- `GET/PUT /api/v1/employees/{employeeId}`
- `GET/POST /api/v1/branches/{branchId}/shifts`
- `GET/POST /api/v1/branches/{branchId}/shift-assignments`
- `POST /api/v1/attendance/check-in`
- `POST /api/v1/attendance/check-out`
- `GET /api/v1/attendance`
- `GET/POST /api/v1/leave-requests`
- `POST /api/v1/leave-requests/{id}/approve`
- `POST /api/v1/leave-requests/{id}/reject`
- `GET /api/v1/reports/payroll-summary`

### 7.3. Menu và inventory
- `GET/POST /api/v1/menu/categories`
- `GET/POST /api/v1/menu/items`
- `GET/PUT /api/v1/menu/items/{itemId}`
- `PUT /api/v1/branches/{branchId}/menu`
- `GET/POST /api/v1/branches/{branchId}/ingredients`
- `GET /api/v1/branches/{branchId}/inventory`
- `POST /api/v1/branches/{branchId}/inventory/receipts`
- `POST /api/v1/branches/{branchId}/inventory/issues`
- `POST /api/v1/branches/{branchId}/inventory/adjustments`
- `GET /api/v1/branches/{branchId}/inventory/ledger`
- `GET /api/v1/branches/{branchId}/inventory/low-stock`

### 7.4. Public ordering
- `GET /api/v1/public/branches/{branchCode}/menu`
- `POST /api/v1/public/orders` — yêu cầu `Idempotency-Key`
- `GET /api/v1/public/orders/{publicTrackingToken}`
- `POST /api/v1/public/orders/{publicTrackingToken}/cancel` chỉ khi chính sách cho phép và đơn chưa ở trạng thái cấm hủy.

### 7.5. Staff orders, payments, invoices, reports
- `GET /api/v1/branches/{branchId}/orders`
- `GET /api/v1/orders/{orderId}`
- `POST /api/v1/orders/{orderId}/confirm`
- `POST /api/v1/orders/{orderId}/start-preparing`
- `POST /api/v1/orders/{orderId}/mark-ready`
- `POST /api/v1/orders/{orderId}/complete`
- `POST /api/v1/orders/{orderId}/cancel`
- `POST /api/v1/orders/{orderId}/payments` — ghi nhận thanh toán theo quyền/chính sách.
- `GET /api/v1/invoices/{invoiceId}`
- `GET /api/v1/invoices/{invoiceId}/pdf`
- `GET /api/v1/reports/revenue`
- `GET /api/v1/reports/revenue/export.csv`
- `GET /api/v1/reports/inventory/export.csv`

Đây là danh sách endpoint mục tiêu, không phải yêu cầu phải tạo endpoint trùng lặp. Ưu tiên resource-oriented design và không tạo route nếu không có use case thực tế. Với từng endpoint, ghi request/response schema, validation, authorization policy, status code và lỗi có thể xảy ra.

---

## 8. Quy chuẩn API và xử lý lỗi

- Pagination mặc định cho danh sách: `page`, `pageSize`; có giới hạn tối đa để tránh tải quá lớn.
- Filtering/sorting phải whitelist trường được hỗ trợ, không nối trực tiếp input vào SQL.
- Dùng `CancellationToken` xuyên suốt I/O.
- Validation server-side cho mọi request.
- HTTP status code phù hợp: 200/201/202/204, 400 validation, 401 unauthenticated, 403 forbidden, 404 not found, 409 conflict/idempotency/state conflict, 429 rate limit, 500 generic unexpected error.
- Dùng `ProblemDetails` có `traceId`/correlation ID; không trả stack trace, SQL detail hoặc secret.
- Idempotency cho thao tác tạo đơn và các lệnh tài chính có khả năng retry.
- Không trả 200 cho thao tác thất bại.
- Tạo OpenAPI examples an toàn, không dùng dữ liệu cá nhân thật.

---

## 9. Bảo mật, privacy và audit

- HTTPS trên môi trường triển khai.
- Secret local qua user secrets/environment; cloud dùng Azure Key Vault hoặc managed identity.
- Least privilege cho database, Service Bus, storage và deployment identity.
- CORS allowlist theo environment.
- Rate limiting cho login và public order/tracking.
- Kiểm tra file export và input chống injection; tránh SQL injection bằng parameterization/EF Core.
- Không ghi access token, password, connection string, payment secret hoặc public tracking token vào log.
- Redact thông tin nhạy cảm; log payload message ở mức tối thiểu.
- Audit log ghi actor, action, resource, branch, timestamp UTC, correlation ID và phần thay đổi cần thiết; không ghi bí mật.
- Có retention policy cấu hình cho audit/logs và dữ liệu cá nhân.
- Có hướng dẫn backup/restore và migration rollback strategy. Với migration phá hủy dữ liệu, yêu cầu backup và triển khai theo expand-contract.
- Dependency scanning và cập nhật package bảo mật trong CI nếu công cụ có sẵn.

---

## 10. Observability và độ tin cậy

- Structured logging (ví dụ `ILogger` với provider phù hợp).
- Correlation ID xuyên HTTP request, outbox và message handler.
- Health endpoints: liveness và readiness; readiness kiểm tra dependencies cần thiết nhưng không làm lộ thông tin.
- Metrics tối thiểu: request count/latency/error, queue processing/failure, dead-letter count, order processing duration, database/cache failures.
- Retry chỉ cho lỗi tạm thời và phải có giới hạn; tránh retry lặp vô hạn.
- Timeout cho outbound calls; cancellation đúng cách.
- Redis không khả dụng: hệ thống phải có fallback hợp lý; không dùng cache làm nguồn dữ liệu chuẩn.
- Broker không khả dụng: lưu yêu cầu và outbox an toàn nếu kiến trúc cho phép; nếu không thể tiếp nhận an toàn thì trả lỗi rõ ràng, không báo thành công giả.
- Có runbook xử lý message dead-letter, lỗi kết nối DB, cache outage, queue backlog và rollback release.

---

## 11. Frontend: các màn hình bắt buộc

### 11.1. Admin/Super Admin
- Login.
- Dashboard toàn chuỗi.
- Quản lý chi nhánh và tài khoản/role.
- Quản lý nhân viên, ca làm, phân ca, nghỉ phép, chấm công.
- Quản lý menu, giá và availability theo chi nhánh.
- Quản lý nguyên liệu, công thức, nhập/xuất/điều chỉnh, ledger, cảnh báo tồn thấp.
- Danh sách đơn, chi tiết đơn và lịch sử trạng thái.
- Hóa đơn, báo cáo doanh thu, lịch sử giao dịch và export.
- Trang lỗi 403/404 và session expired.

### 11.2. Branch Staff/Kitchen
- Màn hình đơn mới theo thời gian thực.
- Bộ lọc theo trạng thái.
- Chi tiết món, variant, add-on, ghi chú.
- Hành động cập nhật trạng thái theo quyền và transition hợp lệ.
- Trạng thái kết nối SignalR, reconnect và tải lại snapshot.
- Màn hình số thứ tự hiển thị cho khách.

### 11.3. Customer
- Menu mobile-first, lọc danh mục.
- Chi tiết món/variant/add-on.
- Giỏ hàng và cập nhật số lượng.
- Xác nhận đơn, hiển thị số thứ tự và trạng thái.
- Trang theo dõi đơn bằng public token.
- Hiển thị lỗi hết món/giá thay đổi/thử lại an toàn.

### 11.4. UI/UX
- Dùng component PrimeVue phù hợp; không tự dựng component phức tạp khi component chuẩn đáp ứng.
- Bảng có loading, empty state, pagination, filter và error state.
- Form có validation message gần trường nhập.
- Confirm dialog cho thao tác hủy, điều chỉnh kho, duyệt/từ chối và thao tác không thể đảo ngược.
- Hỗ trợ keyboard/focus cơ bản, label, contrast và thông báo lỗi rõ ràng.
- Không dùng dữ liệu giả trong production. Seed data chỉ dành cho development/demo và phải được đánh dấu rõ.

---

## 12. Kiểm thử bắt buộc

### 12.1. Unit tests
- Giá, giảm giá, thuế, làm tròn.
- Order state transitions và quy tắc hủy.
- Tính giờ làm, ca qua đêm và chấm công.
- Công thức định lượng và tính tồn kho.
- Authorization/business rules quan trọng.
- Idempotency và consumer duplicate handling ở mức logic.

### 12.2. Integration tests
- EF Core migrations và repository/use cases với SQL Server test instance/container khi môi trường hỗ trợ.
- Login/authorization/branch isolation.
- Tạo đơn, cấp order number đồng thời và idempotency.
- Outbox publish/retry và consumer idempotent.
- Trừ kho một lần, hoàn kho khi hủy theo policy.
- Tạo invoice snapshot và xuất PDF/CSV.
- SignalR authorization/group scope ở mức có thể tự động kiểm thử.
- Redis/broker integration bằng container hoặc test doubles có nêu rõ giới hạn.

### 12.3. E2E/smoke tests
- Admin login → tạo/cấu hình món → menu public hiển thị.
- Customer đặt món → đơn xuất hiện ở màn hình bếp → cập nhật Preparing → Ready → Completed.
- Nhân viên chi nhánh A không thể truy cập dữ liệu chi nhánh B.
- Xuất PDF/CSV thành công với dữ liệu mẫu tiếng Việt.
- Responsive smoke test cho viewport mobile/tablet/desktop nếu tooling có sẵn.

### 12.4. Concurrency và tải
- Test nhiều request tạo đơn đồng thời: không trùng order number trong phạm vi quy định.
- Retry cùng idempotency key không tạo đơn trùng.
- Duplicate message không trừ kho/tạo invoice hai lần.
- Có kịch bản load test cơ bản và báo cáo throughput/latency/error. Không ghi con số hiệu năng nếu chưa chạy đo thực tế.

---

## 13. Local development và Docker

Tạo `docker-compose.yml` hoặc cấu hình tương đương để chạy dependencies local:
- SQL Server Developer hoặc image SQL Server phù hợp license/môi trường.
- Redis.
- RabbitMQ management.
- API và Worker nếu phù hợp với cách phát triển.
- Frontend tùy chọn chạy trong container hoặc bằng Vite dev server.

Yêu cầu:
- Health checks, named volumes cho dữ liệu local, port mapping rõ ràng.
- `.env.example` chỉ có placeholder, không có secret thật.
- Có script migrate/seed development data.
- Có lệnh chạy dự án, test, build và reset dữ liệu local.
- Không tự động chạy destructive migration hoặc xóa volume mà không có cảnh báo rõ.
- README mô tả prerequisites, ports, environment variables và troubleshooting.

---

## 14. Azure và CI/CD

### 14.1. Azure target architecture

Đề xuất triển khai ban đầu:
- Azure App Service hoặc Azure Container Apps cho API/Worker; chọn một hướng chính và ghi rõ lý do.
- Azure SQL Database.
- Azure Cache for Redis (chọn tier/SKU phù hợp khả năng sẵn có tại thời điểm triển khai).
- Azure Service Bus.
- Azure SignalR Service nếu cần scale-out SignalR.
- Azure Key Vault và Managed Identity.
- Azure Monitor / Application Insights.
- Azure Container Registry nếu triển khai container.
- Azure Blob Storage chỉ khi cần lưu asset/export file; không bắt buộc nếu chưa có use case.

Không tự tạo tài nguyên Azure thật, không thực hiện thao tác có chi phí, không deploy production hoặc thay đổi cloud account nếu chưa được người dùng cho phép. Có thể tạo Bicep/Terraform templates và pipeline dưới dạng code.

### 14.2. CI/CD

GitHub Actions tối thiểu:
1. Checkout.
2. Setup .NET/Node phiên bản được pin.
3. Restore dependencies.
4. Format/lint.
5. Unit tests.
6. Integration tests khi dependencies có thể khởi chạy.
7. Build backend và frontend.
8. Build container images nếu áp dụng.
9. Dependency/security checks nếu có.
10. Publish artifacts.

CD:
- Tách environment Development/Staging/Production.
- Dùng GitHub environment approvals cho production.
- Không chứa secrets trong workflow file; dùng OIDC/managed identity khi cấu hình được.
- Migration production là bước có kiểm soát; ưu tiên expand-contract.
- Có health check sau deploy và hướng dẫn rollback.
- Không tự động deploy production khi chưa có phê duyệt.

---

## 15. Chiến lược branch và commit

Đề xuất:
- `main`: trạng thái ổn định/release.
- `develop`: tích hợp nếu team thực sự dùng mô hình này; nếu repository dùng trunk-based thì giữ convention hiện có.
- `feature/<scope>-<short-description>`, `fix/<scope>-<short-description>`.
- Commit theo Conventional Commits, ví dụ:
  - `feat(auth): add staff authentication and branch authorization`
  - `feat(hr): implement attendance and leave requests`
  - `feat(inventory): add inventory ledger and stock adjustments`
  - `feat(orders): add idempotent public order submission`
  - `feat(realtime): publish branch-scoped order updates`
  - `feat(reporting): export invoice PDF and revenue CSV`
  - `test(orders): cover concurrent order number allocation`
  - `docs(deploy): document Azure deployment and rollback`
- Không commit secrets, generated binaries, local database files hoặc file export chứa dữ liệu thật.
- Agent không tự push, mở PR, merge hoặc tạo release nếu chưa có quyền và yêu cầu rõ ràng.

---

## 16. Lộ trình triển khai

Không bắt buộc bám cứng lịch tuần; ưu tiên hoàn thành theo dependency và chất lượng. Lịch 10 tuần chỉ là ước lượng tham khảo.

### Phase 0 — Repository discovery và thiết kế
- Khảo sát codebase.
- Chốt architecture, conventions, ADR, ERD, API error/pagination conventions.
- Tạo implementation plan và risk register.
- **Gate:** kế hoạch phù hợp repository, không phá code hiện có.

### Phase 1 — Skeleton và developer experience
- Backend projects, frontend Vue 3/TypeScript, lint/format, environment configuration.
- Docker Compose, SQL Server, Redis, RabbitMQ.
- Health checks, structured logging, OpenAPI, CI build/test.
- Initial migration và seed data development.
- **Gate:** fresh clone có thể chạy theo README; CI build thành công.

### Phase 2 — Authentication, branch scope và audit
- Login, token handling, user/role/branch assignment.
- Branch CRUD.
- Authorization policies và branch isolation tests.
- **Gate:** tests chứng minh truy cập chéo chi nhánh bị chặn.

### Phase 3 — HR, shifts và attendance
- Employee CRUD, shift configuration/assignment, leave workflow, check-in/out.
- Summary giờ làm và export cơ bản nếu phù hợp.
- **Gate:** validation, ca qua đêm và audit trail được test.

### Phase 4 — Menu và inventory
- Categories/items/variants/add-ons, giá và menu theo chi nhánh.
- Ingredients, recipes, receipt/issue/adjustment, ledger và low-stock alert.
- **Gate:** transaction/concurrency rules và inventory reconciliation tests đạt.

### Phase 5 — Order core và async processing
- Order domain/state machine, public menu/order API, idempotency.
- Atomic order number, outbox, Service Bus/RabbitMQ adapters, worker retry/DLQ.
- Invoice/payment record nội bộ theo phạm vi đã chốt.
- **Gate:** concurrency/duplicate message tests đạt.

### Phase 6 — Frontend ordering và real-time
- Customer ordering flow.
- Kitchen/staff queue và queue display.
- SignalR group authorization, reconnect và snapshot reload.
- **Gate:** E2E từ tạo đơn đến hoàn tất chạy được.

### Phase 7 — Reports, PDF/CSV và dashboard
- PDF invoice K80/A5.
- CSV UTF-8 BOM, formula injection protection.
- Revenue/inventory reports, dashboard và branch filtering.
- **Gate:** kiểm tra dữ liệu đối chiếu và output tiếng Việt.

### Phase 8 — Hardening và Azure readiness
- Security review, dependency checks, performance baseline.
- Azure infrastructure templates, CI/CD, monitoring, backup/restore/runbooks.
- Staging deployment nếu được cấp quyền.
- **Gate:** smoke tests, rollback procedure và operational docs đầy đủ.

---

## 17. Cấu hình môi trường

Tạo `.env.example` và tài liệu biến cấu hình, tối thiểu gồm các nhóm:
- `ASPNETCORE_ENVIRONMENT`
- Connection string SQL Server (placeholder, không có secret thật)
- Redis endpoint/configuration
- Message broker provider (`RabbitMQ` local hoặc `AzureServiceBus` cloud)
- RabbitMQ host/username/password qua secret store
- Azure Service Bus namespace/queue names qua configuration
- JWT issuer/audience/signing key hoặc Key Vault reference
- CORS allowed origins
- Public base URL và frontend API base URL
- Branch timezone/business-day configuration
- Logging/telemetry configuration
- Export storage configuration nếu có

Validate cấu hình khi app khởi động; fail fast với lỗi cấu hình bắt buộc. Không log giá trị secret.

---

## 18. Tiêu chí nghiệm thu tổng thể

Dự án chỉ được coi là MVP hoàn thành khi:

1. Có thể chạy local theo hướng dẫn từ repository mới.
2. Super Admin tạo chi nhánh và quản lý tài khoản/role.
3. Branch Admin chỉ quản lý được dữ liệu chi nhánh được cấp.
4. Nhân viên có thể xem lịch, check-in/out và gửi yêu cầu nghỉ phép.
5. Quản trị viên quản lý được menu, giá, công thức và kho.
6. Khách không đăng nhập vẫn xem menu, tạo đơn và theo dõi đơn bằng token công khai.
7. Tạo đơn đồng thời không tạo trùng số thứ tự trong phạm vi đã định; retry request không tạo đơn trùng.
8. Worker xử lý message có retry/DLQ và không trừ kho/tạo hóa đơn trùng khi nhận duplicate.
9. Trạng thái đơn được cập nhật theo transition hợp lệ và đồng bộ qua SignalR.
10. Có ghi nhận thanh toán nội bộ rõ ràng; không giả lập xác minh thanh toán từ nhà cung cấp ngoài.
11. Có hóa đơn PDF và báo cáo CSV tiếng Việt, giới hạn quyền và chống CSV formula injection.
12. Dashboard/báo cáo lọc đúng theo role, chi nhánh và thời gian.
13. Unit/integration tests cho nghiệp vụ trọng yếu chạy được; build backend/frontend thành công.
14. CI kiểm tra build/test; cấu hình Azure và hướng dẫn deploy/rollback được cung cấp.
15. README, ERD, API docs, assumptions/decisions và runbook được cập nhật.
16. Không có secrets hoặc dữ liệu khách hàng thật trong repository/logs/sample data.

---

## 19. Những điểm agent phải coi là giả định có thể thay đổi

Ghi lại các mục sau trong `docs/assumptions-and-decisions.md`, không âm thầm hard-code:
- Một tổ chức/chuỗi có nhiều chi nhánh trong MVP.
- VND là currency mặc định.
- Timezone mặc định `Asia/Ho_Chi_Minh`, lưu timestamp bằng UTC.
- Order number duy nhất theo chi nhánh và business date; không tái sử dụng số.
- Giá/thuế/giảm giá được snapshot tại thời điểm tạo đơn.
- Trừ kho khi đơn được xác nhận/đưa vào chế biến; hủy sau đó tạo ledger entry hoàn kho.
- Không cho tồn kho âm theo mặc định.
- Thanh toán giai đoạn đầu là ghi nhận nội bộ, chưa tích hợp payment gateway.
- PDF là hóa đơn/biên nhận nội bộ, chưa phải hóa đơn điện tử pháp lý.
- Thuế suất và quy tắc làm tròn phải cấu hình, không mặc định là một tỷ lệ pháp lý cụ thể.
- Chỉ bật Native AOT cho thành phần đã xác nhận tương thích.
- Chưa có tải mục tiêu cụ thể; cần load test để xác định capacity.
- Không tự deploy hoặc tạo tài nguyên cloud có chi phí nếu chưa được chấp thuận.

---

## 20. Prompt thực thi cho AI coding agent

**Bắt đầu triển khai ngay theo chỉ dẫn sau:**

1. Đọc toàn bộ repository và tài liệu hiện có. Không bắt đầu bằng việc sinh hàng loạt file trước khi hiểu codebase.
2. Tạo/cập nhật `docs/implementation-plan.md`, `docs/assumptions-and-decisions.md`, `docs/data-model.md` và danh sách API/ADR cần thiết.
3. Chia công việc thành task nhỏ có thể build/test độc lập. Thực hiện tuần tự theo dependency; không chỉ đưa ra kế hoạch rồi dừng lại.
4. Bắt đầu với Phase 0 và Phase 1, sau đó tiếp tục phase tiếp theo trong cùng phiên làm việc nếu công cụ và thời gian cho phép. Khi bị giới hạn, dừng tại một trạng thái compile/test được và báo task tiếp theo cụ thể.
5. Với mỗi thay đổi, chạy các lệnh kiểm tra phù hợp và xử lý lỗi trước khi tiếp tục. Không bỏ qua test thất bại hoặc giảm chất lượng test chỉ để build xanh.
6. Ưu tiên luồng nghiệp vụ end-to-end hơn số lượng file hoặc số endpoint. Không tạo UI chỉ có dữ liệu tĩnh để tuyên bố hoàn thành.
7. Giữ các tích hợp bên ngoài sau interface, cấu hình bằng environment/Key Vault và có adapter local phù hợp.
8. Bảo đảm branch isolation, idempotency, transaction, outbox, inventory ledger và order state transitions được thiết kế trước khi hoàn thiện luồng order.
9. Nếu gặp yêu cầu mơ hồ, áp dụng giả định trong mục 19, ghi lại quyết định và tiếp tục; chỉ hỏi khi không thể tiếp tục an toàn.
10. Kết thúc mỗi phase bằng báo cáo ngắn gồm: đã làm, file chính, migration, tests/build đã chạy, kết quả thực tế, rủi ro còn lại và phase kế tiếp.

**Kết quả mong đợi:** một hệ thống có thể chạy và kiểm thử được, có kiến trúc rõ ràng, nghiệp vụ nhất quán, bảo mật theo chi nhánh, có tài liệu vận hành và sẵn sàng để đội phát triển tiếp tục mở rộng — không phải chỉ là bản demo giao diện.
