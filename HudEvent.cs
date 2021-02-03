using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace BTKSAImmersiveHud
{
    public class HudEvent : MonoBehaviour
    {
        public bool enableUntilClear = false;

        public List<CallBack> OnEnableListeners = new List<CallBack>();
        public List<DisableCallback> OnDisableListeners = new List<DisableCallback>();

        public delegate void CallBack(bool enableUntilClear);
        public delegate void DisableCallback();

        private float lastEnableTime = 0;

        public HudEvent(IntPtr obj0) : base(obj0)
        {

        }

        public void OnDisable()
        {
            foreach(DisableCallback listener in OnDisableListeners)
            {
                listener();
            }
        }

        public void OnEnable()
        {
            if (Time.time > (lastEnableTime + .2f))
            {
                
                foreach (CallBack listener in OnEnableListeners)
                {
                    listener(enableUntilClear);
                }
            }

            lastEnableTime = Time.time;
        }
    }
}
