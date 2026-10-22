# 透明图标

桌面图标和悬浮球来自用户提供的蓝色眼睛图案，经内置image_gen透明背景编辑后制作。eye-master.png保留高分辨率透明母图；eye.png为运行时资源，app.ico包含16/24/32/48/64/128/256像素七种尺寸。

转换时使用高质量双三次采样和等比例居中，PNG及每个ICO尺寸都保留透明通道。悬浮球使用Windows逐像素Alpha，不以白色或其他颜色填充圆外区域，并按系统DPI等比例绘制。

图像编辑提示词：

> Edit the supplied original app icon. Use case: background-extraction. Remove ONLY the white background outside the dark blue circular disk, making the four exterior corners truly transparent with alpha. Preserve the complete original circular disk, the eye, cyan central star, golden stars, blue colors, layout, circular proportions and fine details faithfully. Do not redesign, redraw, stretch, warp, crop the disk, add padding, add borders, shadows, text or new elements. Keep square canvas and original 1:1 aspect ratio, centered full-size circle. Crisp antialiased circumference, no white fringe or white matte, clean transparent pixels outside. Output a high resolution transparent PNG suitable for high DPI Windows application icons and an 80px desktop floating ball.

24项资源检查覆盖PNG四角透明、七种ICO尺寸及Alpha、不同DPI尺寸绘制、非正方形区域等比例适配。
