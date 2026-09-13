/**
 * `<input type="file">` 的 `accept` 值 —— 全站「图片位」共用一份。
 *
 * 与后端 `LocalFileStorageService.AllowedExtensions` 的**图片子集**对齐：
 * 后端还接受 `.ico` / `.mp4` / `.webm` / `.pdf` / `.zip`（通用文件接口），
 * 但那不是头像 / 封面 / Logo / 首屏背景图该出现的东西，所以这里收窄。
 *
 * ⚠️ **刻意不含 `image/svg+xml`**：SVG 可以内嵌 `<script>`，而本站的文件读取接口是
 * **同源**下发的 —— 直接打开 `/api/files/xxx.svg` 会让脚本在站点源上执行，
 * 构成存储型 XSS（曾登记为缺口 G11，2026-09-13 通过「不支持上传 SVG」关闭）。
 * 详见 `Blog.Infrastructure/Files/LocalFileStorageService.cs` 里 `AllowedExtensions`
 * 的注释。
 *
 * 注意这只是**文件选择器的过滤**，用户仍可手动选「所有文件」——
 * 真正的拦截在服务端，前端做这件事只是让正常用户没机会选错。
 */
export const ACCEPT_IMAGE = 'image/png,image/jpeg,image/webp,image/gif'
