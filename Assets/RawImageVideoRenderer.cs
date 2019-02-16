﻿using System;
using UnityEngine;
using UnityEngine.UI; //RawImage

public class RawImageVideoRenderer : MonoBehaviour
{
	private IntPtr nhvd;
	private Texture2D videoTexture = null;

	void Awake()
	{
		NHVD.nhvd_hw_config hw_config = new NHVD.nhvd_hw_config{hardware="vaapi", codec="h264", device="/dev/dri/renderD128", pixel_format="bgr0"};
		NHVD.nhvd_net_config net_config = new NHVD.nhvd_net_config{ip="", port=9766, timeout_ms=500 };

		nhvd=NHVD.nhvd_init (ref net_config, ref hw_config);

		if (nhvd == IntPtr.Zero)
			Debug.Log ("failed to initialize NHVD");

		AdaptTexture (640, 360, TextureFormat.BGRA32);

	}
	void OnDestroy()
	{
		NHVD.nhvd_close (nhvd);
	}

	private void AdaptTexture(int width, int height, TextureFormat format)
	{
		if (videoTexture == null || width != videoTexture.width || height != videoTexture.height || format != videoTexture.format)
		{
			videoTexture = new Texture2D (width, height, format, false);
			GetComponent<RawImage> ().texture = videoTexture;
		}
	}
		
	// Update is called once per frame
	void Update ()
	{
		int w=0, h=0, s=0;
		//	IntPtr data = GetImageDataBegin (ref w, ref h, ref s);
		IntPtr data = NHVD.nhvd_get_frame_begin(nhvd, ref w, ref h, ref s);

		if (data != IntPtr.Zero)
		{
			AdaptTexture (w, h, TextureFormat.BGRA32);
			videoTexture.LoadRawTextureData (data, w * h * 4);
			videoTexture.Apply (false);
		}

		if (NHVD.nhvd_get_frame_end (nhvd) != 0)
			Debug.LogWarning ("Failed to get NHVD frame data");
	}
}
