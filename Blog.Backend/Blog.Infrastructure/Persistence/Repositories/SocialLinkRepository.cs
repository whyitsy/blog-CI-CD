using Blog.Domain.Entities;
using Blog.Domain.IRepository;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class SocialLinkRepository : BaseRepository<SocialLink>, ISocialLinkRepository
    {
        public SocialLinkRepository(BlogDbContext context) : base(context)
        {
        }
    }
}
