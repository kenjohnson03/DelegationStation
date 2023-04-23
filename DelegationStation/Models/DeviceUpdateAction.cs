using Newtonsoft.Json;

namespace DelegationStation.Models
{
    public class DeviceUpdateAction
    {
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public DeviceUpdateActionType ActionType { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }

        public DeviceUpdateAction() 
        { 
            this.Id = Guid.NewGuid();
            this.ActionType = DeviceUpdateActionType.Attribute;
            this.Name = "";
            this.Value = "";
        }
        public DeviceUpdateAction(Guid id, DeviceUpdateActionType actionType, string name, string value)
        {
            Id = id;
            ActionType = actionType;
            Name = name;
            Value = value;
        }
    }

    public enum DeviceUpdateActionType
    {
        Attribute,
        Group
    }
}
