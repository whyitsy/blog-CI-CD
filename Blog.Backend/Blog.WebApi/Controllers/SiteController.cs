using Blog.Application.Common;
using Blog.Application.Services.Site;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>站点：首屏配置 / 社交链接 / Footer 统计</summary>
    [ApiController]
    [Route("api/site")]
    public class SiteController : ControllerBase
    {
        private readonly ISiteService _site;

        public SiteController(ISiteService site)
        {
            _site = site;
        }

        /// <summary>首屏配置：站点名、打字机副标题列表、背景图、建站日期</summary>
        [HttpGet("config")]
        public async Task<ApiResponse<SiteConfigDto>> GetConfig(CancellationToken cancellationToken)
        {
            var config = await _site.GetConfigAsync(cancellationToken);
            return ApiResponse<SiteConfigDto>.Ok(config);
        }

        /// <summary>逐项更新站点配置（仅管理员，乐观锁）</summary>
        [HttpPut("config")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ApiResponse<SiteConfigDto>> UpdateConfig([FromBody] UpdateSiteConfigRequest request, CancellationToken cancellationToken)
        {
            var config = await _site.UpdateConfigAsync(request, cancellationToken);
            return ApiResponse<SiteConfigDto>.Ok(config);
        }

        /// <summary>
        /// 首屏底部社交图标（Github、Bilibili 等）。
        /// **公开读**：公开首屏需要只取可见项，因此本接口不鉴权。
        /// </summary>
        /// <param name="includeHidden">管理端配置页传 true 以同时返回隐藏项；公开首屏默认只取可见项</param>
        [HttpGet("social-links")]
        public async Task<ApiResponse<List<SocialLinkDto>>> GetSocialLinks(
            [FromQuery] bool includeHidden = false,
            CancellationToken cancellationToken = default)
        {
            var links = await _site.GetSocialLinksAsync(includeHidden, cancellationToken);
            return ApiResponse<List<SocialLinkDto>>.Ok(links);
        }

        /// <summary>批量新增/更新社交链接（仅管理员，逐条乐观锁）</summary>
        [HttpPut("social-links")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ApiResponse<List<SocialLinkDto>>> SaveSocialLinks([FromBody] List<UpsertSocialLinkRequest> request, CancellationToken cancellationToken)
        {
            var links = await _site.SaveSocialLinksAsync(request, cancellationToken);
            return ApiResponse<List<SocialLinkDto>>.Ok(links);
        }

        /// <summary>删除社交链接（仅管理员，软删除，需携带当前版本号）</summary>
        [HttpDelete("social-links/{id:guid}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ApiResponse<object?>> DeleteSocialLink(Guid id, [FromQuery] int version, CancellationToken cancellationToken)
        {
            await _site.DeleteSocialLinkAsync(id, version, cancellationToken);
            return ApiResponse.Ok();
        }

        /// <summary>Footer 统计：建站天数 / 总字数 / 浏览次数等</summary>
        [HttpGet("stats")]
        public async Task<ApiResponse<SiteStatsDto>> GetStats(CancellationToken cancellationToken)
        {
            var stats = await _site.GetStatsAsync(cancellationToken);
            return ApiResponse<SiteStatsDto>.Ok(stats);
        }
    }
}
