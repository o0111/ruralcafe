//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace RuralCafe.Database
{
    using System;
    using System.Collections.Generic;
    
    public partial class UserCacheDomain
    {
        public UserCacheDomain()
        {
            this.UserCacheItems = new HashSet<UserCacheItem>();
        }
    
        public int userID { get; set; }
        public string domain { get; set; }
    
        public virtual ICollection<UserCacheItem> UserCacheItems { get; set; }
    }
}