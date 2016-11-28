﻿using System;
using System.Collections.Generic;
using UnityEngine;
using FairyGUI.Utils;

namespace FairyGUI
{
	/// <summary>
	/// 
	/// </summary>
	public enum FitScreen
	{
		None,
		FitSize,
		FitWidthAndSetMiddle,
		FitHeightAndSetCenter
	}

	/// <summary>
	/// 
	/// </summary>
	[ExecuteInEditMode]
	[AddComponentMenu("FairyGUI/UI Panel")]
	public class UIPanel : MonoBehaviour, EMRenderTarget
	{
		/// <summary>
		/// 
		/// </summary>
		public Container container { get; private set; }

		/// <summary>
		/// 
		/// </summary>
		public string packageName;

		/// <summary>
		/// 
		/// </summary>
		public string componentName;

		/// <summary>
		/// 
		/// </summary>
		public FitScreen fitScreen;

		/// <summary>
		/// 
		/// </summary>
		public int sortingOrder;

		[SerializeField]
		string packagePath;
		[SerializeField]
		RenderMode renderMode = RenderMode.ScreenSpaceOverlay;
		[SerializeField]
		Camera renderCamera = null;
		[SerializeField]
		Vector3 position;
		[SerializeField]
		Vector3 scale = new Vector3(1, 1, 1);
		[SerializeField]
		Vector3 rotation = new Vector3(0, 0, 0);
		[SerializeField]
		bool fairyBatching = false;
		[SerializeField]
		bool touchDisabled = false;
		[SerializeField]
		Vector2 cachedUISize;
		[SerializeField]
		HitTestMode hitTestMode = HitTestMode.Default;
		[SerializeField]
		bool setNativeChildrenOrder = false;

		[System.NonSerialized]
		int screenSizeVer;
		[System.NonSerialized]
		Rect uiBounds; //Track bounds even when UI is not created, edit mode

		GComponent _ui;
		[NonSerialized]
		bool _created;

		List<Renderer> _renders;

		void OnEnable()
		{
			if (Application.isPlaying)
			{
				if (this.container == null)
				{
					CreateContainer();

					if (!string.IsNullOrEmpty(packagePath) && UIPackage.GetByName(packageName) == null)
						UIPackage.AddPackage(packagePath);
				}
				else
					this.container._disabled = false;
			}
			else
			{
				//不在播放状态时我们不在OnEnable创建，因为Prefab也会调用OnEnable，延迟到Update里创建（Prefab不调用Update)
				//每次播放前都会disable/enable一次。。。
				if (container != null)//如果不为null，可能是因为Prefab revert， 而不是因为Assembly reload，
					OnDestroy();

				EMRenderSupport.Add(this);
				screenSizeVer = 0;
				uiBounds.position = position;
				uiBounds.size = cachedUISize;
				if (uiBounds.size == Vector2.zero)
					uiBounds.size = new Vector2(30, 30);
			}
		}

		void OnDisable()
		{
			if (Application.isPlaying)
			{
				if (this.container != null)
					this.container._disabled = true;
			}
			else
				EMRenderSupport.Remove(this);
		}

		void Start()
		{
			if (!_created && Application.isPlaying)
				CreateUI_PlayMode();
		}

		void Update()
		{
			if (screenSizeVer != StageCamera.screenSizeVer)
				HandleScreenSizeChanged();
		}

		void OnDestroy()
		{
			if (container != null)
			{
				if (!Application.isPlaying)
					EMRenderSupport.Remove(this);

				if (_ui != null)
				{
					_ui.Dispose();
					_ui = null;
				}

				container.Dispose();
				container = null;
			}

			_renders = null;
		}

		void CreateContainer()
		{
			if (!Application.isPlaying)
			{
				Transform t = this.transform;
				int cnt = t.childCount;
				while (cnt > 0)
				{
					GameObject go = t.GetChild(cnt - 1).gameObject;
					if (go.name == "GComponent")
						UnityEngine.Object.DestroyImmediate(go);
					cnt--;
				}
			}

			this.container = new Container(this.gameObject);
			this.container.renderMode = renderMode;
			this.container.renderCamera = renderCamera;
			this.container.touchable = !touchDisabled;
			this.container._panelOrder = sortingOrder;
			this.container.fairyBatching = fairyBatching;
			if (Application.isPlaying)
			{
				SetSortingOrder(this.sortingOrder, true);
				if (this.hitTestMode == HitTestMode.Raycast)
					this.container.hitArea = new BoxColliderHitTest(this.gameObject.AddComponent<BoxCollider>());

				if (setNativeChildrenOrder)
				{
					CacheNativeChildrenRenderers();

					this.container.onUpdate = () =>
					{
						int cnt = _renders.Count;
						int sv = UpdateContext.current.renderingOrder++;
						for (int i = 0; i < cnt; i++)
						{
							Renderer r = _renders[i];
							if (r != null)
								_renders[i].sortingOrder = sv;
						}
					};
				}
			}
		}

		/// <summary>
		/// 
		/// </summary>
		public GComponent ui
		{
			get
			{
				if (!_created && Application.isPlaying)
					CreateUI_PlayMode();

				return _ui;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		public void CreateUI()
		{
			if (_ui != null)
			{
				_ui.Dispose();
				_ui = null;
			}

			CreateUI_PlayMode();
		}

		/// <summary>
		/// Change the sorting order of the panel in runtime.
		/// </summary>
		/// <param name="value">sorting order value</param>
		/// <param name="apply">false if you dont want the default sorting behavior. e.g. call Stage.SortWorldSpacePanelsByZOrder later.</param>
		public void SetSortingOrder(int value, bool apply)
		{
			this.sortingOrder = value;
			container._panelOrder = value;

			if (apply)
				Stage.inst.ApplyPanelOrder(container);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="value"></param>
		public void SetHitTestMode(HitTestMode value)
		{
			if (this.hitTestMode != value)
			{
				this.hitTestMode = value;
				BoxCollider collider = this.gameObject.GetComponent<BoxCollider>();
				if (this.hitTestMode == HitTestMode.Raycast)
				{
					if (collider == null)
						collider = this.gameObject.AddComponent<BoxCollider>();
					this.container.hitArea = new BoxColliderHitTest(collider);
					if (_ui != null)
						UpdateHitArea();
				}
				else
				{
					this.container.hitArea = null;
					if (collider != null)
						Component.Destroy(collider);
				}
			}
		}

		/// <summary>
		/// 
		/// </summary>
		public void CacheNativeChildrenRenderers()
		{
			if (_renders == null)
				_renders = new List<Renderer>();
			else
				_renders.Clear();

			Transform t = this.container.cachedTransform;
			int cnt = t.childCount;
			for (int i = 0; i < cnt; i++)
			{
				GameObject go = t.GetChild(i).gameObject;
				if (go.name != "GComponent")
					_renders.AddRange(go.GetComponentsInChildren<Renderer>(true));
			}

			cnt = _renders.Count;
			for (int i = 0; i < cnt; i++)
			{
				Renderer r = _renders[i];
				if ((r is SkinnedMeshRenderer) || (r is MeshRenderer))
				{
					//Set the object rendering in Transparent Queue as UI objects
					if (r.sharedMaterial != null)
						r.sharedMaterial.renderQueue = 3000;
				}
			}
		}

		void CreateUI_PlayMode()
		{
			_created = true;

			if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(componentName))
				return;

			_ui = (GComponent)UIPackage.CreateObject(packageName, componentName);
			if (_ui != null)
			{
				_ui.position = position;
				if (scale.x != 0 && scale.y != 0)
					_ui.scale = scale;
				_ui.rotationX = rotation.x;
				_ui.rotationY = rotation.y;
				_ui.rotation = rotation.z;
				if (this.container.hitArea != null)
				{
					UpdateHitArea();
					_ui.onSizeChanged.Add(UpdateHitArea);
					_ui.onPositionChanged.Add(UpdateHitArea);
				}
				this.container.AddChildAt(_ui.displayObject, 0);

				HandleScreenSizeChanged();
			}
			else
				Debug.LogError("Create " + componentName + "@" + packageName + " failed!");
		}

		void UpdateHitArea()
		{
			((BoxColliderHitTest)this.container.hitArea).SetArea(_ui.x, _ui.y, _ui.width, _ui.height);
		}

		void CreateUI_EditMode()
		{
			if (!EMRenderSupport.packageListReady || UIPackage.GetByName(packageName) == null)
				return;

#if UNITY_5
			UIObjectFactory.packageItemExtensions.Clear();
			UIObjectFactory.loaderConstructor = null;
			DisplayOptions.SetEditModeHideFlags();
			DisplayOptions.defaultRoot = this.transform;
			_ui = (GComponent)UIPackage.CreateObject(packageName, componentName);
			DisplayOptions.defaultRoot = null;

			if (_ui != null)
			{
				_ui.position = position;
				if (scale.x != 0 && scale.y != 0)
					_ui.scale = scale;
				_ui.rotationX = rotation.x;
				_ui.rotationY = rotation.y;
				_ui.rotation = rotation.z;
				this.container.AddChildAt(_ui.displayObject, 0);

				cachedUISize = _ui.size;
				uiBounds.size = cachedUISize;
				HandleScreenSizeChanged();
			}
#else
			PackageItem pi = UIPackage.GetByName(packageName).GetItemByName(componentName);
			if (pi != null)
			{
				cachedUISize = new Vector2(pi.width, pi.height);
				uiBounds.size = cachedUISize;
				HandleScreenSizeChanged();
			}
#endif
		}

		void HandleScreenSizeChanged()
		{
			if (!Application.isPlaying)
			{
				DisplayOptions.SetEditModeHideFlags();
				DisplayOptions.defaultRoot = this.transform;
			}

			screenSizeVer = StageCamera.screenSizeVer;

			if (this.container != null)
			{
				if (this.container.renderMode != RenderMode.WorldSpace)
					this.container.scale = new Vector2(StageCamera.UnitsPerPixel * UIContentScaler.scaleFactor, StageCamera.UnitsPerPixel * UIContentScaler.scaleFactor);
			}

			int width = Mathf.CeilToInt(Screen.width / UIContentScaler.scaleFactor);
			int height = Mathf.CeilToInt(Screen.height / UIContentScaler.scaleFactor);
			if (this._ui != null)
			{
				switch (fitScreen)
				{
					case FitScreen.FitSize:
						this._ui.SetXY(0, 0);
						this._ui.SetSize(width, height);
						break;

					case FitScreen.FitWidthAndSetMiddle:
						this._ui.SetXY(0, (int)((height - this._ui.sourceHeight) / 2));
						this._ui.SetSize(width, this._ui.sourceHeight);
						break;

					case FitScreen.FitHeightAndSetCenter:
						this._ui.SetXY((int)((width - this._ui.sourceWidth) / 2), 0);
						this._ui.SetSize(this._ui.sourceWidth, height);
						break;
				}
			}
			else
			{
				switch (fitScreen)
				{
					case FitScreen.FitSize:
						uiBounds.position = new Vector2(0, 0);
						uiBounds.size = new Vector2(width, height);
						break;

					case FitScreen.FitWidthAndSetMiddle:
						uiBounds.position = new Vector2(0, (int)((height - cachedUISize.y) / 2));
						uiBounds.size = new Vector2(width, cachedUISize.y);
						break;

					case FitScreen.FitHeightAndSetCenter:
						uiBounds.position = new Vector2((int)((width - cachedUISize.x) / 2), 0);
						uiBounds.size = new Vector2(cachedUISize.x, height);
						break;
				}
			}
		}

		#region edit mode functions

		void OnUpdateSource(object[] data)
		{
			if (Application.isPlaying)
				return;

			this.packageName = (string)data[0];
			this.packagePath = (string)data[1];
			this.componentName = (string)data[2];

			if ((bool)data[3])
			{
				if (container == null)
					return;

				if (_ui != null)
				{
					_ui.Dispose();
					_ui = null;
				}
			}
		}

		public void ApplyModifiedProperties(bool sortingOrderChanged, bool fitScreenChanged)
		{
			if (container != null)
			{
				container.renderMode = renderMode;
				container.renderCamera = renderCamera;
				if (sortingOrderChanged)
				{
					container._panelOrder = sortingOrder;
					if (Application.isPlaying)
						SetSortingOrder(sortingOrder, true);
					else
						EMRenderSupport.orderChanged = true;
				}
				container.fairyBatching = fairyBatching;
			}

			if (_ui != null)
			{
				if (fitScreen == FitScreen.None)
					_ui.position = position;
				if (scale.x != 0 && scale.y != 0)
					_ui.scale = scale;
				_ui.rotationX = rotation.x;
				_ui.rotationY = rotation.y;
				_ui.rotation = rotation.z;
			}
			if (fitScreen == FitScreen.None)
				uiBounds.position = position;
			screenSizeVer = 0;//force HandleScreenSizeChanged be called

			if (fitScreenChanged && this.fitScreen == FitScreen.None)
			{
				if (this._ui != null)
					this._ui.SetSize(this._ui.sourceWidth, this._ui.sourceHeight);
				uiBounds.size = cachedUISize;
			}
		}

		public void MoveUI(Vector3 delta)
		{
			if (fitScreen != FitScreen.None)
				return;

			this.position += delta;
			if (_ui != null)
				_ui.position = position;
			uiBounds.position = position;
		}

		public Vector3 GetUIWorldPosition()
		{
			if (_ui != null)
				return _ui.displayObject.cachedTransform.position;
			else
				return this.container.cachedTransform.TransformPoint(uiBounds.position);
		}

		void OnDrawGizmos()
		{
			if (Application.isPlaying || this.container == null)
				return;

			Vector3 pos, size;
			if (this._ui != null)
			{
				Gizmos.matrix = this._ui.displayObject.cachedTransform.localToWorldMatrix;
				pos = new Vector3(this._ui.width / 2, -this._ui.height / 2, 0);
				size = new Vector3(this._ui.width, this._ui.height, 0);
			}
			else
			{
				Gizmos.matrix = this.container.cachedTransform.localToWorldMatrix;
				pos = new Vector3(uiBounds.x + uiBounds.width / 2, -uiBounds.y - uiBounds.height / 2, 0);
				size = new Vector3(uiBounds.width, uiBounds.height, 0);
			}

			Gizmos.color = new Color(0, 0, 0, 0);
			Gizmos.DrawCube(pos, size);

			Gizmos.color = Color.white;
			Gizmos.DrawWireCube(pos, size);
		}

		public int EM_sortingOrder
		{
			get { return sortingOrder; }
		}

		public void EM_BeforeUpdate()
		{
			if (container == null)
				CreateContainer();

			if (packageName != null && componentName != null && _ui == null)
				CreateUI_EditMode();

			if (screenSizeVer != StageCamera.screenSizeVer)
				HandleScreenSizeChanged();
		}

		public void EM_Update(UpdateContext context)
		{
			DisplayOptions.SetEditModeHideFlags();
			DisplayOptions.defaultRoot = this.transform;

			container.Update(context);

			DisplayOptions.defaultRoot = null;

			if (setNativeChildrenOrder)
			{
				CacheNativeChildrenRenderers();

				int cnt = _renders.Count;
				int sv = context.renderingOrder++;
				for (int i = 0; i < cnt; i++)
				{
					Renderer r = _renders[i];
					if (r != null)
						r.sortingOrder = sv;
				}
			}
		}

		public void EM_Reload()
		{
			if (_ui != null)
			{
				_ui.Dispose();
				_ui = null;
			}
		}

		#endregion
	}
}
