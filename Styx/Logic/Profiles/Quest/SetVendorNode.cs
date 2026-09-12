using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class SetVendorNode : OrderNode
    {
        public SetVendorNode(List<Vendor> vendors)
            : this(vendors, null)
        {
        }

        private SetVendorNode(List<Vendor> vendors, XElement element)
            : base(OrderNodeType.SetVendor, element)
        {
            this.Vendors = vendors;
        }

        public SetVendorNode(Vendor vendor)
            : this(new List<Vendor>() { vendor })
        {
        }

        public List<Vendor> Vendors { get; private set; }

        public static SetVendorNode FromXml(XElement element)
        {
            // Look for either <Vendor> or <Vendors> child element
            var vendorElement = element.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("vendor", StringComparison.OrdinalIgnoreCase));
            var vendorsElement = element.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("vendors", StringComparison.OrdinalIgnoreCase));

            XElement targetElement = vendorElement ?? vendorsElement;
            if (targetElement == null)
                throw new ProfileMissingElementException("Vendor", element);

            if (targetElement.Name.LocalName.Equals("vendor", StringComparison.OrdinalIgnoreCase))
            {
                // Single vendor
                Vendor vendor;
                try
                {
                    vendor = new Vendor(targetElement);
                }
                catch (ProfileException ex)
                {
                    throw new ProfileException("Could not parse SetVendor node", ex);
                }
                return new SetVendorNode(new List<Vendor> { vendor }, element);
            }

            // Multiple vendors
            var vendors = new List<Vendor>();
            foreach (var childElement in targetElement.Elements()
                .Where(e => e.Name.LocalName.Equals("vendor", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    vendors.Add(new Vendor(childElement));
                }
                catch (ProfileException ex)
                {
                    throw new ProfileException("Could not parse SetVendor node", ex);
                }
            }
            return new SetVendorNode(vendors, element);
        }

        public override string ToString()
        {
            return $"[SetVendorNode: Vendors: {string.Join(", ", Vendors.ConvertAll(v => v.ToString()).ToArray())}]";
        }
    }
}
