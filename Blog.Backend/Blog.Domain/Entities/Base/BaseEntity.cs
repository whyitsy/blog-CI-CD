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
