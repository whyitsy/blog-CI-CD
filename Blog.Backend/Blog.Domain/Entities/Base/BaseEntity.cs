using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Domain.Entities.Base
{
    public class BaseEntity
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }

        /// <summary>
        /// 乐观锁整数版本号（从 1 开始）。
        /// 由仓储层手动控制：更新时以客户端持有的版本号作 SQL 条件，
        /// 生成 UPDATE ... SET "Version" = @expected + 1 WHERE "Id" = @id AND "Version" = @expected。
        /// 版本不匹配时 0 行受影响，EF 抛出 DbUpdateConcurrencyException。
        /// </summary>
        public int Version { get; protected set; } = 1;

        /// <summary>
        /// 修改实体的删除状态为已删除，并设置删除时间为当前时间
        /// 不保存，由服务调用方的uow进行保存
        /// </summary>
        public void Delete()
        {
            IsDeleted = true;
            DeletedAt = DateTimeOffset.UtcNow;
        }
    }
}
