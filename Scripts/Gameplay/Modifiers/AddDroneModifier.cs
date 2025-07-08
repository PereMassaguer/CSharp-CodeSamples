using UnityEngine;

[CreateAssetMenu(menuName = "Modifier/AddDrone")]
public class AddDroneModifier : AbstractModifier
{
    public GameObject dronePrefab;

    protected override void OnActivate(GameObject parent)
    {
        var droneController = parent.GetComponent<DroneCompanionController>();
        if (!droneController) return;

        if (droneController.FreeSockets <= 0)
        {
            var inventory = parent.GetComponent<InventoryComponent>();
            if (!inventory) return;

            var droped = inventory.DropItemWithModifier<AddDroneModifier>();
            if (!droped)
            {
                Debug.LogError("Inventory could NOT drop any existing drone item");
                return;
            }
        }

        if (droneController.FreeSockets > 0)
        {
            droneController.AddDrone(dronePrefab);
        }
    }

    protected override void OnDeactivate(GameObject parent)
    {
        var droneController = parent.GetComponent<DroneCompanionController>();
        if (!droneController) return;

        droneController.RemoveDrone(dronePrefab);
    }
}
