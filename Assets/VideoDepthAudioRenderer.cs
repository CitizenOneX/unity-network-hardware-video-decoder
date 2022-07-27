/*
 * Unity Network Hardware Video Decoder
 * 
 * Copyright 2019 (C) Bartosz Meglicki <meglickib@gmail.com>
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 */

using System;
using UnityEngine;

public class VideoDepthAudioRenderer : MonoBehaviour
{
	private string hardwareDepth = "cuda";
	private string hardwareTexture = "cuda";
	private string codecDepth = "hevc_cuvid";
	private string codecTexture = "hevc_cuvid";
	private string deviceDepth = "";
	private string deviceTexture = "";
	private int widthDepth = 0;     // automatically detected at runtime
	private int widthTexture = 0;   // aligned streams match resolutions
	private int heightDepth = 0;
	private int heightTexture = 0;
	private string pixel_formatDepth = "p010le";
	private string pixel_formatTexture = "nv12";

	private string ip = "";
	private ushort port = 9766;
	private int timeout_ms = 0; // no timeout


	private IntPtr unhvd;

	private UNHVD.unhvd_frame[] frame = new UNHVD.unhvd_frame[]
	{
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }, // depth
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }, // color
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }  // aux (raw PCM audio)
	};

	private Texture2D colorTexture;

	void Awake()
	{
		// trim down the debug messages to not include stack traces
		Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

		UNHVD.unhvd_net_config net_config = new UNHVD.unhvd_net_config { ip = this.ip, port = this.port, timeout_ms = this.timeout_ms };

		UNHVD.unhvd_hw_config[] hw_config = new UNHVD.unhvd_hw_config[]
		{
			new UNHVD.unhvd_hw_config{hardware=this.hardwareDepth, codec=this.codecDepth, device=this.deviceDepth, pixel_format=this.pixel_formatDepth, width=this.widthDepth, height=this.heightDepth, profile=0},
			new UNHVD.unhvd_hw_config{hardware=this.hardwareTexture, codec=this.codecTexture, device=this.deviceTexture, pixel_format=this.pixel_formatTexture, width=this.widthTexture, height=this.heightTexture, profile=0}
		};

		// TODO use depth config if we need to use unprojection code
		//UNHVD.unhvd_depth_config dc = new UNHVD.unhvd_depth_config { ppx = 319.809f, ppy = 236.507f, fx = 606.767f, fy = 607.194f, depth_unit = 0.0001f, min_margin = 0.19f, max_margin = 0.01f };

		unhvd = UNHVD.unhvd_init(ref net_config, hw_config, hw_config.Length, 1, IntPtr.Zero); //ref dc);

		if (unhvd == IntPtr.Zero)
		{
			Debug.Log ("failed to initialize UNHVD");
			gameObject.SetActive (false);
		}

		//flip the texture mapping upside down
		Vector2[] uv = GetComponent<MeshFilter>().mesh.uv;
		for (int i = 0; i < uv.Length; ++i)
			uv [i][1] = -uv [i][1];
		GetComponent<MeshFilter> ().mesh.uv = uv;
	}
	void OnDestroy()
	{
		UNHVD.unhvd_close (unhvd);
	}

	private void AdaptTexture()
	{
		if (colorTexture== null || colorTexture.width != frame[1].width || colorTexture.height != frame[1].height)
		{
			Debug.Log(string.Format("Texture plane format: {0} linesizes: ({1}, {2}, {3})", frame[1].format, frame[1].linesize[0], frame[1].linesize[1], frame[1].linesize[2]));
			colorTexture = new Texture2D (frame[1].width, frame[1].height, TextureFormat.RGB24, false);
			GetComponent<Renderer> ().material.mainTexture = colorTexture;
		}
	}

	// Update is called once per frame
	void LateUpdate ()
	{
		if (UNHVD.unhvd_get_frame_begin(unhvd, frame) == 0)
		{
			// if a color texture frame is present, ensure existing color Texture2D
			// matches width/height then copy data over
			if (frame[1].data != null)
			{
				AdaptTexture();
				var data = colorTexture.GetRawTextureData<byte>();
				int pixels = frame[1].width * frame[1].height;
				unsafe
				{
					for (int index = 0; index < pixels; index++)
					{
						// NV12 has a Y plane and a UV-interleaved plane
						// load the w x h luma first as grayscale
						byte Y = ((byte*)frame[1].data[0])[index];
						data[3 * index] = Y;
						data[3 * index + 1] = Y;
						data[3 * index + 2] = Y;
					}
				}
				colorTexture.Apply(false);
			}

			// if audio is present...
			// TODO
		}

		if (UNHVD.unhvd_get_frame_end (unhvd) != 0)
			Debug.LogWarning ("Failed to get UNHVD frame data");
	}
}
