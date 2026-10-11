extends SceneTree


func _initialize():
	var root = (get_script().resource_path as String).get_base_dir().get_base_dir()
	for suffix in ["", "_outline"]:
		var source = root.path_join("TheArchitect/images/map/the_watcher%s.svg" % suffix)
		var image = Image.new()
		var result = image.load_svg_from_string(FileAccess.get_file_as_string(source), 4.0)
		if result != OK:
			push_error("Could not rasterize %s: %s" % [source, result])
			quit(1)
			return
		image.resize(278, 278, Image.INTERPOLATE_LANCZOS)
		# Keep tintable ink white even where filtering mixes it with transparent pixels.
		for y in image.get_height():
			for x in image.get_width():
				image.set_pixel(x, y, Color(1, 1, 1, image.get_pixel(x, y).a))
		result = image.save_png(source.replace(".svg", ".png"))
		if result != OK:
			push_error("Could not save %s: %s" % [source, result])
			quit(1)
			return
	quit()
