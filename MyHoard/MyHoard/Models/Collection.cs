using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyHoard.Models
{
    public class Collection : BaseEntity
    {
        private string name;
        private string description;
        private string thumbnail;
        private int itemsNumber;
        private DateTime createdDate;
        private DateTime modifiedDate;
        
        public string Name
        { get { return name; }
            set
            {
                name = value;
            }
        }
        public string Description 
        {
            get { return description; }
            set
            {
                description = value;
            }
        }

        public string Thumbnail
        {
            get { return thumbnail; }
            set
            {
                thumbnail = value;
            }
        }

        public int ItemsNumber 
        {
            get { return itemsNumber; }
            set
            {
                itemsNumber = value;
            }
        }
        
        [Ignore]
        public List<string> Tags { get; set; }

        public DateTime CreatedDate
        {
            get { return createdDate; } 
            set
            {
                createdDate=value;
            } 
        }
        public DateTime ModifiedDate
        {
            get { return modifiedDate; }
            set
            {
                modifiedDate = value;
            }
        }
    }
}
