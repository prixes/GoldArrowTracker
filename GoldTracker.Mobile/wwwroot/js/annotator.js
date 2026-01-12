function getImageNaturalSize(image) {
    return {
        width: image.naturalWidth,
        height: image.naturalHeight
    };
}

var annotator = {
    init: function (canvas, image, dotNetHelper) {
        this.canvas = canvas;
        this.image = image;
        this.dotNetHelper = dotNetHelper;
        this.ctx = canvas.getContext('2d');
        this.isDrawing = false;
        this.startX = 0;
        this.startY = 0;
        this.boxes = [];

        // Resize canvas to match image
        this.resizeCanvas();

        // Event listeners
        this.canvas.addEventListener('mousedown', this.startDrawing.bind(this));
        this.canvas.addEventListener('mousemove', this.draw.bind(this));
        this.canvas.addEventListener('mouseup', this.stopDrawing.bind(this));
        this.canvas.addEventListener('mouseleave', this.stopDrawing.bind(this));

        // Touch events
        this.canvas.addEventListener('touchstart', this.startDrawing.bind(this));
        this.canvas.addEventListener('touchmove', this.draw.bind(this));
        this.canvas.addEventListener('touchend', this.stopDrawing.bind(this));

        window.addEventListener('resize', this.resizeCanvas.bind(this));
    },

    resizeCanvas: function () {
        this.canvas.width = this.image.clientWidth;
        this.canvas.height = this.image.clientHeight;
        this.redraw();
    },

    startDrawing: function (e) {
        if (e.cancelable) e.preventDefault();
        this.isDrawing = true;
        const pos = this.getCanvasCoordinates(e);
        this.startX = pos.x;
        this.startY = pos.y;
    },

    draw: function (e) {
        if (!this.isDrawing) return;
        if (e.cancelable) e.preventDefault();
        const pos = this.getCanvasCoordinates(e);
        this.redraw(); // Redraw existing boxes
        this.ctx.strokeStyle = 'red';
        this.ctx.lineWidth = 2;

        // Center-to-Radius logic
        // Start point is the Center
        const centerX = this.startX;
        const centerY = this.startY;

        // Distance is the Radius
        const dx = pos.x - centerX;
        const dy = pos.y - centerY;
        const radius = Math.sqrt(dx * dx + dy * dy);

        this.ctx.beginPath();
        this.ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
        this.ctx.stroke();
    },

    stopDrawing: function (e) {
        if (!this.isDrawing) return;
        if (e.cancelable) e.preventDefault();
        this.isDrawing = false;
        const pos = this.getCanvasCoordinates(e);

        // Center-to-Radius logic for Bounding Box
        const centerX = this.startX;
        const centerY = this.startY;
        const dx = pos.x - centerX;
        const dy = pos.y - centerY;
        const radius = Math.sqrt(dx * dx + dy * dy);

        // Convert to bounding box for Object Detection logic
        const box = {
            startX: centerX - radius,
            startY: centerY - radius,
            endX: centerX + radius,
            endY: centerY + radius
        };

        // Ensure box has a size
        if (radius > 2) {
            console.log("Canvas Size:", this.canvas.width, this.canvas.height);
            this.dotNetHelper.invokeMethodAsync('AddBox',
                box.startX / this.canvas.width,
                box.startY / this.canvas.height,
                box.endX / this.canvas.width,
                box.endY / this.canvas.height);
        }
    },

    addBox: function (box) {
        // box here has normalized coordinates from .NET (0 to 1)
        this.boxes.push(box);
        this.redraw();
    },

    removeLastBox: function () {
        this.boxes.pop();
        this.redraw();
    },

    getCanvasCoordinates: function (e) {
        const rect = this.canvas.getBoundingClientRect();
        let clientX, clientY;

        if (e.touches && e.touches.length > 0) {
            clientX = e.touches[0].clientX;
            clientY = e.touches[0].clientY;
        } else if (e.changedTouches && e.changedTouches.length > 0) {
            clientX = e.changedTouches[0].clientX;
            clientY = e.changedTouches[0].clientY;
        } else {
            clientX = e.clientX;
            clientY = e.clientY;
        }

        return {
            x: (clientX - rect.left) * (this.canvas.width / rect.width),
            y: (clientY - rect.top) * (this.canvas.height / rect.height)
        };
    },

    redraw: function () {
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        this.boxes.forEach(box => {
            const sx = box.startX * this.canvas.width;
            const sy = box.startY * this.canvas.height;
            const ex = box.endX * this.canvas.width;
            const ey = box.endY * this.canvas.height;

            const width = ex - sx;
            const height = ey - sy;
            const radius = Math.max(width, height) / 2;
            const centerX = sx + width / 2;
            const centerY = sy + height / 2;

            this.ctx.strokeStyle = box.color || 'lime';
            this.ctx.lineWidth = 2;

            this.ctx.beginPath();
            this.ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
            this.ctx.stroke();

            this.ctx.fillStyle = box.color || 'lime';
            this.ctx.font = 'bold 16px sans-serif';
            const labelY = sy > 20 ? sy - 5 : sy + 20;
            this.ctx.fillText(box.label, sx, labelY);
        });
    },

    clear: function () {
        this.boxes = [];
        this.redraw();
    },

    destroy: function () {
        // Clean up
        this.canvas.replaceWith(this.canvas.cloneNode(true));
    }
};