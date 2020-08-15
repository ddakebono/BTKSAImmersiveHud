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
        public List<CallBack> onEnableListeners = new List<CallBack>();

        public delegate void CallBack();

        public HudEvent(IntPtr obj0) : base(obj0)
        {

        }

        public void OnEnable()
        {
            foreach(CallBack listener in onEnableListeners)
            {
                listener();
            }
        }
    }
}
