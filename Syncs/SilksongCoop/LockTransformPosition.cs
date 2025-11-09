// Decompiled with JetBrains decompiler
// Type: SilksongCoop.LockTransformPosition
// Assembly: ClassLibrary1, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 901D39ED-0492-4306-A98E-FB496E06AC71
// Assembly location: D:\Temp\Temp\sk\silksongcoop\SilksongCoop.dll

using UnityEngine;

#nullable disable
namespace SilklessCoopVisual.Syncs.SilksongCoop;

public class LockTransformPosition : MonoBehaviour
{
  public Vector3 lockedPos;
  public bool locked = true;
  private float _lastUpdateTime;

  public void UpdateLockedPos(Vector3 pos)
  {
    lockedPos = pos;
    _lastUpdateTime = Time.time;
    locked = true;
  }

  private void LateUpdate()
  {
    if (!locked)
      return;
    if (Time.time - (double) _lastUpdateTime > 1.0)
      locked = false;
    else
      transform.position = lockedPos;
  }
}
