using Blog.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Domain.IRepository
{
    public interface ICategoryRepository: IBaseRepository<Category>
    {
    }
}
