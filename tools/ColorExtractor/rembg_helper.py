import sys
from rembg import remove
from PIL import Image
import io
import urllib.request

def remove_background(image_url, output_path):
    req = urllib.request.Request(image_url, headers={'User-Agent': 'Mozilla/5.0'})
    with urllib.request.urlopen(req) as response:
        input_data = response.read()

    output_data = remove(input_data)
    
    img = Image.open(io.BytesIO(output_data)).convert("RGBA")
    img.save(output_path, "PNG")

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: python rembg_helper.py <image_url> <output_path>")
        sys.exit(1)
    
    image_url = sys.argv[1]
    output_path = sys.argv[2]
    
    try:
        remove_background(image_url, output_path)
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)
