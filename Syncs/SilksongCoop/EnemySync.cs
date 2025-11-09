// Decompiled with JetBrains decompiler
// Type: SilksongCoop.EnemySync
// Assembly: ClassLibrary1, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 901D39ED-0492-4306-A98E-FB496E06AC71
// Assembly location: D:\Temp\Temp\sk\silksongcoop\SilksongCoop.dll

using SilklessCoopVisual.Syncs.SilksongCoop;
using SilksongCoop;
using UnityEngine;

#nullable enable
namespace SilklessCoopVisual.Syncs.SilksongCoop;

public static class EnemySync
{
    public static void ApplyEnemyDelta(SteamCoopPlugin.EnemyDelta d)
    {
        GameObject byId = EnemyRegistry.FindById(d.id, d.scene);
        if (byId == null)
        {
            SteamCoopPlugin.Logger.LogWarning((object)$"[EnemySync] Enemy {d.id} still not found after refresh.");
        }
        else
        {
            HealthManager component = byId.GetComponent<HealthManager>();
            if (component == null || byId.GetComponent<SyncDeadMarker>() != null)
                return;
            if (d.dead.HasValue && d.dead.Value)
            {
                if (!component.GetIsDead())
                    component.Die(new float?(), (AttackTypes)1, true);
                byId.SetActive(false);
                byId.AddComponent<SyncDeadMarker>();
            }
            else
            {
                if (d.hp.HasValue && !component.GetIsDead())
                    component.hp = d.hp.Value;
                LockTransformPosition transformPosition = byId.GetComponent<LockTransformPosition>();
                if (transformPosition == null)
                    transformPosition = byId.AddComponent<LockTransformPosition>();
                transformPosition.UpdateLockedPos(d.pos);
            }
        }
    }
}
