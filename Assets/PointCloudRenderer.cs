/*
 * Unity Network Hardware Video Decoder
 * 
 * Copyright 2020 (C) Bartosz Meglicki <meglickib@gmail.com>
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 */

using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PointCloudRenderer : MonoBehaviour
{
	private string hardwareDepth = "";
	private string hardwareTexture = "";
	private string codecDepth = "hevc";
	private string codecTexture = "hevc";
	private string deviceDepth = "";
	private string deviceTexture = "";
	private int widthDepth = 320;
	private int heightDepth = 240;
	private int widthTexture = 640;
	private int heightTexture = 480;
	private string pixel_formatDepth = "p010le";
	private string pixel_formatTexture = "yuv420p";
	private string ip = "";
	private ushort port = 9766;


	private int framecounter = 0;

	private IntPtr unhvd;
	private UNHVD.unhvd_frame frame = new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] };
	private UNHVD.unhvd_point_cloud point_cloud = new UNHVD.unhvd_point_cloud {data = System.IntPtr.Zero, size=0, used=0};

	private Mesh mesh;

	void Awake()
	{
		UNHVD.unhvd_net_config net_config = new UNHVD.unhvd_net_config{ip=this.ip, port=this.port, timeout_ms=500 };
		UNHVD.unhvd_hw_config[] hw_config = new UNHVD.unhvd_hw_config[]
		{
			new UNHVD.unhvd_hw_config{hardware=this.hardwareDepth, codec=this.codecDepth, device=this.deviceDepth, pixel_format=this.pixel_formatDepth, width=this.widthDepth, height=this.heightDepth, profile=2}, // profile FF_PROFILE_HEVC_MAIN_10
//			new UNHVD.unhvd_hw_config{hardware=this.hardwareTexture, codec=this.codecTexture, device=this.deviceTexture, pixel_format=this.pixel_formatTexture, width=this.widthTexture, height=this.heightTexture, profile=1} // FF_PROFILE_HEVC_MAIN
		};

		//For depth config explanation see:
		//https://github.com/bmegli/unity-network-hardware-video-decoder/wiki/Point-clouds-configuration

		//For MinZ formula see BKMs_Tuning_RealSense_D4xx_Cam.pdf
		//For D435 at 848x480 the MinZ is ~16.8cm, in our result unit min_margin is 0.168
		//max_margin is arbitrarilly set

		//DepthConfig dc = new DepthConfig {ppx = 421.353f, ppy=240.93f, fx=426.768f, fy=426.768f, depth_unit = 0.0001f, min_margin = 0.168f, max_margin = 0.01f };
		//DepthConfig dc = new DepthConfig {ppx = 421.353f, ppy=240.93f, fx=426.768f, fy=426.768f, depth_unit = 0.0000390625f, min_margin = 0.168f, max_margin = 0.01f};
		//DepthConfig dc - new DepthConfig {ppx = 421.353f, ppy=240.93f, fx=426.768f, fy=426.768f, depth_unit = 0.00003125f, min_margin = 0.168f, max_margin = 0.01f};

		//sample config for depth + color, depth aligned to 848x480 color (so we use color intrinsics, not depth intrinsics)
		//DepthConfig dc = new DepthConfig{ppx = 425.038f, ppy=249.114f, fx=618.377f, fy=618.411f, depth_unit = 0.0001f, min_margin = 0.168f, max_margin = 0.01f};

		//sample config for L515 320x240 with depth units resulting in 6.4 mm precision and 6.5472 m range (alignment to depth)
		DepthConfig dc = new DepthConfig{ppx = 168.805f, ppy=125.068f, fx=229.699f, fy=230.305f, depth_unit = 0.0001f*60, min_margin = 0.19f, max_margin = 0.01f};

		//sample config for L515 640x480 with depth units resulting in 2.5 mm precision and 2.5575 m range (alignment to depth)
		//DepthConfig dc = new DepthConfig { ppx = 358.781f, ppy = 246.297f, fx = 470.941f, fy = 470.762f, depth_unit = 0.0000390625f, min_margin = 0.19f, max_margin = 0.01f };

		//sample config for L515 1280x720 with depth units resulting in 2.5 mm precision and 2.5575 m range (alignment to color)
		//DepthConfig dc = new DepthConfig{ppx = 647.881f, ppy=368.939f, fx=906.795f, fy=906.768f, depth_unit = 0.0000390625f, min_margin = 0.19f, max_margin = 0.01f};
		//DepthConfig dc = new DepthConfig{ppx = 647.881f, ppy=368.939f, fx=906.795f, fy=906.768f, depth_unit = 0.000250f, min_margin = 0.19f, max_margin = 0.01f};

		//sample config for D455 848x480 with depth units resulting in 2.5 mm precision and 2.5575 m range, MinZ at 848x480 is 350 mm, for depth, depth + ir, depth aligned color
		//DepthConfig dc = new DepthConfig{ppx = 426.33f, ppy=239.446f, fx=422.768f, fy=422.768f, depth_unit = 0.0000390625f, min_margin = 0.35f, max_margin = 0.01f};
		//as above, alignment to color, distortion model ignored
		//DepthConfig dc = new DepthConfig{ppx = 419.278f, ppy=244.24f, fx=419.909f, fy=418.804f, depth_unit = 0.0000390625f, min_margin = 0.35f, max_margin = 0.01f};

		UNHVD.unhvd_depth_config depth_config = new UNHVD.unhvd_depth_config{ppx = dc.ppx, ppy = dc.ppy, fx = dc.fx, fy = dc.fy, depth_unit =  dc.depth_unit, min_margin = dc.min_margin, max_margin = dc.max_margin};

		Debug.Log(string.Format("hw_config Length: {0}", hw_config.Length));
		unhvd = UNHVD.unhvd_init (ref net_config, hw_config, hw_config.Length, ref depth_config);

		if (unhvd == IntPtr.Zero)
		{
			Debug.Log ("failed to initialize UNHVD");
			gameObject.SetActive (false);
		}		
	}
	void OnDestroy()
	{
		UNHVD.unhvd_close (unhvd);
	}

	private void PrepareMesh(int size)
	{
		if(mesh == null)
			mesh = new Mesh();

		if(mesh.vertexCount == size)
			return;

		mesh.MarkDynamic();

		//we don't want to recalculate bounds for half million dynamic points so just set wide bounds
		mesh.bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(10, 10, 10));

		//make Unity internal mesh data match our native mesh data (separate streams for position and colors)
		VertexAttributeDescriptor[] layout = new[]
		{
			new VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
			new VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Color, VertexAttributeFormat.UInt32, 4, 1),
		};

		mesh.SetVertexBufferParams(size, layout);

		mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
		int[] indices = new int[size];
		for(int i=0;i<size;++i)
			indices[i] = i;

		mesh.SetIndices(indices, MeshTopology.Points,0);

		// TODO temp create vertexbuffer data
		//Vector3[] positions = new Vector3[size];
		//Color32[] colors = new Color32[size];
		//for (int x=0; x<320; x++)
  //      {
		//	for (int z=0; z<240; z++)
  //          {
		//		positions[z * 320 + x] = new Vector3((x - 160)/100.0f, (float)Math.Sin(x * z), (z - 120)/100.0f);
		//		colors[z * 320 + x] = Color.blue;
  //          }
  //      }
		//mesh.SetVertexBufferData(positions, 0, 0, size, 0);  //, MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds
		//mesh.SetVertexBufferData(colors, 0, 0, size, 1);
		// end TODO

		GetComponent<MeshFilter>().mesh = mesh;
	}

	void LateUpdate ()
	{
		

		if (UNHVD.unhvd_get_point_cloud_begin(unhvd, ref point_cloud) == 0)
		{
			PrepareMesh(point_cloud.size);

            //possible optimization - only render non-zero points (point_cloud.used)
            unsafe
            {
				NativeArray<Vector3> pc = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Vector3>(point_cloud.data.ToPointer(), point_cloud.size, Allocator.None);
                NativeArray<Color32> colors = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Color32>(point_cloud.colors.ToPointer(), point_cloud.size, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
				NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref pc, AtomicSafetyHandle.Create());
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref colors, AtomicSafetyHandle.Create());
#endif

                mesh.SetVertexBufferData(pc, 0, 0, point_cloud.size, 0, MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetVertexBufferData(colors, 0, 0, point_cloud.size, 1, MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds);

				if (++framecounter % 300 == 0)
				{
					Debug.Log(string.Format("PrepareMesh called: {0}, pos: {1}, col: {2}", point_cloud.size, pc[0].ToString(), colors[0].ToString()));
					Debug.Log(string.Format("PrepareMesh called: {0}, pos: {1}, col: {2}", point_cloud.size, pc[320 * 120 + 160].ToString(), colors[320 * 120 + 160].ToString()));
					// FIXME do this once at frame 300 just to see what the bounds are
					mesh.RecalculateBounds();
					Debug.Log(string.Format("Mesh bounds: {0}", mesh.bounds.ToString()));
				}
			}
		}

		if (UNHVD.unhvd_get_point_cloud_end (unhvd) != 0)
			Debug.LogWarning ("Failed to get UNHVD point cloud data");
	}
}
