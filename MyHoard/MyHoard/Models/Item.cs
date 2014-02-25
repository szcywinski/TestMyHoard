using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyHoard.Models
{
    public class Item : BaseEntity
    {
  //      id = string
  //name = string
  //description = string
  //tags = []
  //location = {
  //  lat = float
  //  lng = float
  //}
  //quantity = int
  //media = []  # list of fk_media_id
  //created_date = date
  //modified_date = date
  //collection = string  # fk_collection_id
  //owner = string  # fk_user_username

        private string name;
        private string description;
        private string tags;
        private float locationLat;
        private float locationLng;
        private int quantity;
        private DateTime createdDate;
        private DateTime modifiedDate;
        public DateTime CreatedDate
        {
            get { return createdDate; }
            set
            {
                createdDate = value;
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

        public int Quantity
        {
            get { return quantity; }
            set { quantity = value; }
        }
        public float LocationLng
        {
            get { return locationLng; }
            set { locationLng = value; }
        }

        public float LocationLat
        {
            get { return locationLat; }
            set { locationLat = value; }
        }
        public string Tags
        {
            get { return tags; }
            set { tags = value; }
        }

        public string Description
        {
            get { return description; }
            set { description = value; }
        }
        public string Name
        {
            get { return name; }
            set { name = value; }
        }
    }
}
