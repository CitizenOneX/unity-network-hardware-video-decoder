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

[RequireComponent(typeof(AudioSource))]
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
	private int timeout_ms = 5000; // 5 seconds to wait for sender to start, otherwise won't recover. If set to 0 (no timeout), Unity can't seem to stop the process without hanging/being killed

	private IntPtr unhvd;

	// note: 3 frames in stream (depth, color, audio) but audio is only handled natively in unhvd and doesn't come up through Unity
	private UNHVD.unhvd_frame[] frame = new UNHVD.unhvd_frame[]
	{
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }, // depth
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }, // color
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }  // aux (raw PCM audio)
	};

	private Texture2D colorTextureY;

	// some performance statistics for the FPS Display (logger)
	public int VideoFrameNumber = 0;

	void Awake()
	{
		// trim down the debug messages to not include stack traces
		Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
		
		//NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
		//NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled;

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

		// don't limit the frame rate, we want to sample packets frequently
		QualitySettings.vSyncCount = 0;
	}

	void OnDestroy()
	{
		UNHVD.unhvd_close (unhvd);
	}

	private void AdaptTexture()
	{
		if (colorTextureY == null || colorTextureY.width != frame[1].width || colorTextureY.height != frame[1].height)
		{
			Debug.Log(string.Format("Texture plane format: {0} linesizes: ({1}, {2}, {3})", frame[1].format, frame[1].linesize[0], frame[1].linesize[1], frame[1].linesize[2]));
			colorTextureY = new Texture2D (frame[1].width, frame[1].height, TextureFormat.R8, false);
			GetComponent<Renderer> ().material.mainTexture = colorTextureY;
		}
	}

	// Update is called once per frame
	void Update()
	{
		
		if (UNHVD.unhvd_get_frame_begin(unhvd, frame) == 0)
		{
			// if a color texture frame is present, ensure existing color Texture2D
			// matches width/height then copy data over
			// looks like data is hanging around in data[0] so I'll check linesizes are non-zero too
			if (frame[1].data != null && frame[1].data[0] != null && frame[1].linesize != null && frame[1].linesize[0] > 0)
			{
				++VideoFrameNumber;

				AdaptTexture();
				colorTextureY.LoadRawTextureData(frame[1].data[0], frame[1].linesize[0] * frame[1].height);

				// TODO copy the U and V frames (packed together in NV12 on Windows/NVIDIA) to separate
				// textures and combine in shader
				colorTextureY.Apply(false);
			}
		}

		if (UNHVD.unhvd_get_frame_end(unhvd) != 0)
			Debug.LogWarning("Failed to get UNHVD frame data");
	}
}
